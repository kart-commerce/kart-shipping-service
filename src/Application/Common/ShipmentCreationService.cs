using System.Diagnostics;
using System.Text.Json;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Domain.Entities;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kart.Shipping.Application.Common;

/// <inheritdoc cref="IShipmentCreationService" />
public sealed class ShipmentCreationService(IShippingDbContext dbContext, IDateTimeProvider dateTimeProvider, ILogger<ShipmentCreationService> logger) : IShipmentCreationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShipmentCreationOutcome> CreateAsync(OrderId orderId, Address address, string actor, CancellationToken cancellationToken)
    {
        // ddd-model.md's Idempotent-consumption invariant: the pre-carrier-call existence check.
        // A hit here (including a still-Pending row) is a no-op, never a second carrier call.
        var existing = await dbContext.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Stage {Stage}: Shipment already exists for order {OrderId}; treating as no-op", "ShipmentCreationSkippedDuplicate", orderId);
            return new ShipmentCreationOutcome(existing.Id, AlreadyExisted: true);
        }

        var shipment = Shipment.CreateIntent(orderId);
        shipment.CreatedBy = actor;
        shipment.UpdatedBy = actor;
        dbContext.Shipments.Add(shipment);

        var payload = new CarrierCallRequestedPayload(orderId.ToString(), new AddressDto(address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country));
        var outboxEvent = ShipmentOutboxEvent.Create(shipment.Id, ShipmentOutboxEventType.CarrierCallRequested, JsonSerializer.Serialize(payload, SerializerOptions), dateTimeProvider.UtcNow, Activity.Current?.Id);
        outboxEvent.CreatedBy = actor;
        outboxEvent.UpdatedBy = actor;
        dbContext.ShipmentOutboxEvents.Add(outboxEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // A concurrently-processed redelivered copy of the same OrderConfirmed may have won
            // the race - the UNIQUE(order_id) constraint backstops this as a no-op, never a 500
            // (ddd-model.md's race-handling invariant). Deliberately provider-agnostic (no
            // Npgsql/PostgresException reference here - Application stays free of Infrastructure's
            // database-technology choice): if a row now exists for this orderId, this was that
            // race, not a genuine failure; otherwise the original exception is rethrown unchanged.
            var winner = await dbContext.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            logger.LogInformation(ex, "Stage {Stage}: concurrent creation race for order {OrderId} lost to UNIQUE(order_id); treating as no-op", "ShipmentCreationRaceLost", orderId);
            return new ShipmentCreationOutcome(winner.Id, AlreadyExisted: true);
        }

        logger.LogInformation("Stage {Stage}: shipment {ShipmentId} persisted for order {OrderId}, outbox event {OutboxEventId}", "ShipmentPersisted", shipment.Id, orderId, outboxEvent.Id);

        return new ShipmentCreationOutcome(shipment.Id, AlreadyExisted: false);
    }
}
