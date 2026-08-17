using System.Text.Json;
using Kart.Shared.Observability;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Infrastructure.Persistence;
using Kart.Shipping.Infrastructure.Persistence.ReadModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kart.Shipping.Infrastructure.Messaging;

/// <summary>
/// The CQRS sync mechanism (contracts/README.md deviation #1) - an in-process poller reading
/// EVERY `shipment_outbox` row (published or not, ordered by `outbox_seq`), not a RabbitMQ
/// self-consumer like kart-payment-service's own read-model sync. Reason: the internal
/// `CarrierCallRequested` marker (needed so `Pending` shipments are visible to `ListShipments`)
/// is never published to RabbitMQ at all - a self-consumer bound to `shipping.exchange` could
/// never see it. Claims a batch via `SELECT ... FOR UPDATE SKIP LOCKED`; each individual Mongo
/// upsert is independently race-safe via <see cref="ReadModelProjectionWriter"/>'s
/// `LastAppliedSeq` guard, so batching here (unlike SHIP-2's one-row-per-transaction worker) is
/// safe even if a row's Mongo write partially fails and this transaction retries.
/// </summary>
public sealed class ReadModelProjectionHostedService(IServiceScopeFactory scopeFactory, ILogger<ReadModelProjectionHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 50;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = await ProjectPendingBatchAsync(stoppingToken);
            if (processedCount == 0)
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>
    /// The ENTIRE method - including the claim query and transaction handling, not just the
    /// per-row projection logic - is one try/catch. A transient failure here (Postgres briefly
    /// unreachable, a not-yet-migrated database in a test) must never propagate out of
    /// <see cref="ExecuteAsync"/>: by default, .NET 6+ stops the ENTIRE HOST if a
    /// <see cref="BackgroundService"/>'s <c>ExecuteAsync</c> throws
    /// (<c>BackgroundServiceExceptionBehavior.StopHost</c>) - discovered live via a real
    /// integration-test run where a migration-timing race briefly left this table missing and
    /// took the whole process down instead of just this one poll tick failing.
    /// </summary>
    private async Task<int> ProjectPendingBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<ReadModelProjectionWriter>();

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;

        try
        {
            transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

            var claimed = await dbContext.ShipmentOutboxEvents.FromSqlInterpolated(
                    $"SELECT * FROM shipment_outbox WHERE projected_at IS NULL ORDER BY outbox_seq FOR UPDATE SKIP LOCKED LIMIT {BatchSize}")
                .ToListAsync(stoppingToken);

            if (claimed.Count == 0)
            {
                await transaction.RollbackAsync(stoppingToken);
                return 0;
            }

            using var flowScope = KartFlowContext.Push(FlowNames.ShipmentFulfillment);

            foreach (var outboxEvent in claimed)
            {
                try
                {
                    await ApplyAsync(writer, outboxEvent.ShipmentId.Value, outboxEvent.MessageType, outboxEvent.Payload, outboxEvent.OccurredAt.UtcDateTime, outboxEvent.OutboxSeq, stoppingToken);
                    logger.LogInformation("Stage {Stage}: read model updated for shipment {ShipmentId} from {MessageType} (seq {Seq})", "ReadModelPersisted", outboxEvent.ShipmentId, outboxEvent.MessageType, outboxEvent.OutboxSeq);
                }
                catch (Exception ex)
                {
                    // A single row's Mongo write failing (e.g. transient Mongo unavailability)
                    // must not block projecting the rest of the batch or wedge this row forever -
                    // log and leave `projected_at` null so it's reclaimed next poll tick.
                    logger.LogError(ex, "Read-model projection failed for outbox event {OutboxEventId}; will retry next poll.", outboxEvent.Id);
                    continue;
                }

                outboxEvent.MarkProjected(DateTimeOffset.UtcNow);
                outboxEvent.UpdatedBy = SystemPrincipals.ReadModelProjector;
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            await transaction.CommitAsync(stoppingToken);
            return claimed.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Read-model projector poll tick failed; will retry next poll.");

            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(stoppingToken);
                }
                catch (Exception rollbackEx)
                {
                    logger.LogError(rollbackEx, "Failed to roll back read-model projector transaction after an earlier failure.");
                }
            }

            return 0;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static Task ApplyAsync(ReadModelProjectionWriter writer, Guid shipmentId, ShipmentOutboxEventType messageType, string payloadJson, DateTime occurredAt, long seq, CancellationToken cancellationToken)
    {
        switch (messageType)
        {
            case ShipmentOutboxEventType.CarrierCallRequested:
            {
                var payload = Deserialize<CarrierCallRequestedPayload>(payloadJson);
                return writer.ApplyPendingAsync(shipmentId, payload.OrderId, occurredAt, seq, cancellationToken);
            }
            case ShipmentOutboxEventType.ShipmentDispatched:
            {
                var payload = Deserialize<ShipmentDispatchedPayload>(payloadJson);
                return writer.ApplyDispatchedAsync(shipmentId, payload.OrderId, payload.Carrier, payload.TrackingId, occurredAt, seq, cancellationToken);
            }
            case ShipmentOutboxEventType.ShipmentCreationFailed:
            {
                var payload = Deserialize<ShipmentCreationFailedPayload>(payloadJson);
                return writer.ApplyFailedAsync(shipmentId, payload.OrderId, payload.Reason, occurredAt, seq, cancellationToken);
            }
            default:
                throw new InvalidOperationException($"Read-model projector has no handling for message type '{messageType}'.");
        }
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? throw new InvalidOperationException($"{typeof(T).Name} payload deserialized to null.");
}
