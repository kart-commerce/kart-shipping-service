using System.Text.Json;
using Kart.Shared.Observability;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Domain.Entities;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Domain.ValueObjects;
using Kart.Shipping.Infrastructure.Carriers;
using Kart.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kart.Shipping.Infrastructure.Messaging;

/// <summary>
/// SHIP-2 - the out-of-band worker that resolves a `CarrierCallRequested` marker into a terminal
/// `Dispatched`/`Failed` outcome. Claims ONE row at a time via `SELECT ... FOR UPDATE SKIP LOCKED`
/// inside its own short transaction (never a multi-row batch transaction) - this way a genuine
/// failure mid-processing rolls back only that one row's changes, and multiple horizontally-scaled
/// worker instances never process the same row twice (database-design.md).
/// </summary>
public sealed class CarrierCallWorkerHostedService(IServiceScopeFactory scopeFactory, ILogger<CarrierCallWorkerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int MaxClaimsPerTick = 20;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimedAny = false;

            for (var i = 0; i < MaxClaimsPerTick; i++)
            {
                var processed = await TryProcessOneAsync(stoppingToken);
                if (!processed)
                {
                    break;
                }

                claimedAny = true;
            }

            if (!claimedAny)
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>
    /// The ENTIRE method body - including the claim query itself, not just the resolution logic
    /// after a row is claimed - is one try/catch. A transient failure here (Postgres briefly
    /// unreachable, a not-yet-migrated database in a test) must never propagate out of
    /// <see cref="ExecuteAsync"/>: by default, .NET 6+ stops the ENTIRE HOST if a
    /// <see cref="BackgroundService"/>'s <c>ExecuteAsync</c> throws
    /// (<c>BackgroundServiceExceptionBehavior.StopHost</c>) - discovered live via a real
    /// integration-test run where a migration-timing race briefly left this table missing and
    /// took the whole process down instead of just this one poll tick failing.
    /// </summary>
    private async Task<bool> TryProcessOneAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICarrierDispatcher>();

        IDbContextTransaction? transaction = null;

        try
        {
            transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

            var claimed = await dbContext.ShipmentOutboxEvents.FromSqlInterpolated(
                    $"SELECT * FROM shipment_outbox WHERE message_type = 'CarrierCallRequested' AND processed_at IS NULL ORDER BY outbox_seq FOR UPDATE SKIP LOCKED LIMIT 1")
                .ToListAsync(stoppingToken);

            var outboxEvent = claimed.FirstOrDefault();
            if (outboxEvent is null)
            {
                await transaction.RollbackAsync(stoppingToken);
                return false;
            }

            using var flowScope = KartFlowContext.Push(FlowNames.ShipmentFulfillment);
            var payload = JsonSerializer.Deserialize<CarrierCallRequestedPayload>(outboxEvent.Payload, SerializerOptions)
                ?? throw new InvalidOperationException("CarrierCallRequested payload deserialized to null.");

            var shipment = await dbContext.Shipments.FirstAsync(s => s.Id == outboxEvent.ShipmentId, stoppingToken);

            if (shipment.Status != ShipmentStatus.Pending)
            {
                // Already terminal (a redelivered/duplicate resolution) - mark this marker
                // processed and do nothing else (ddd-model.md's monotonic-transition invariant).
                outboxEvent.MarkProcessed(DateTimeOffset.UtcNow);
                outboxEvent.UpdatedBy = SystemPrincipals.CarrierCallWorker;
            }
            else
            {
                var address = Address.Create(payload.Address.Line1, payload.Address.Line2, payload.Address.City, payload.Address.State, payload.Address.PostalCode, payload.Address.Country);
                var result = await dispatcher.DispatchAsync(address, stoppingToken);

                ShipmentOutboxEvent resolvedEvent;
                if (result.Succeeded)
                {
                    shipment.MarkDispatched(Carrier.From(result.Carrier!), TrackingId.From(result.TrackingId!));
                    var dispatchedPayload = new ShipmentDispatchedPayload(payload.OrderId, result.Carrier!, result.TrackingId!);
                    resolvedEvent = ShipmentOutboxEvent.Create(shipment.Id, ShipmentOutboxEventType.ShipmentDispatched, JsonSerializer.Serialize(dispatchedPayload, SerializerOptions), DateTimeOffset.UtcNow, null);
                    logger.LogInformation("Stage {Stage}: carrier call resolved for shipment {ShipmentId} - Dispatched via {Carrier}", "CarrierCallResolved", shipment.Id, result.Carrier);
                }
                else
                {
                    shipment.MarkFailed(FailureReason.From(result.FailureReason!));
                    var failedPayload = new ShipmentCreationFailedPayload(payload.OrderId, result.FailureReason!);
                    resolvedEvent = ShipmentOutboxEvent.Create(shipment.Id, ShipmentOutboxEventType.ShipmentCreationFailed, JsonSerializer.Serialize(failedPayload, SerializerOptions), DateTimeOffset.UtcNow, null);
                    logger.LogInformation("Stage {Stage}: carrier call resolved for shipment {ShipmentId} - Failed: {Reason}", "CarrierCallResolved", shipment.Id, result.FailureReason);
                }

                shipment.UpdatedBy = SystemPrincipals.CarrierCallWorker;
                resolvedEvent.CreatedBy = SystemPrincipals.CarrierCallWorker;
                resolvedEvent.UpdatedBy = SystemPrincipals.CarrierCallWorker;
                dbContext.ShipmentOutboxEvents.Add(resolvedEvent);

                outboxEvent.MarkProcessed(DateTimeOffset.UtcNow);
                outboxEvent.UpdatedBy = SystemPrincipals.CarrierCallWorker;
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            await transaction.CommitAsync(stoppingToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Carrier-call worker poll tick failed; will retry next poll.");

            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(stoppingToken);
                }
                catch (Exception rollbackEx)
                {
                    logger.LogError(rollbackEx, "Failed to roll back carrier-call worker transaction after an earlier failure.");
                }
            }

            return false;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
