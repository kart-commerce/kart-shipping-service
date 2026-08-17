using Kart.Shipping.Domain.Common;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Domain.ValueObjects;

namespace Kart.Shipping.Domain.Entities;

/// <summary>
/// One row of the transactional outbox (`shipment_outbox`, database-design.md). Written in the
/// same transaction as the `Shipment` mutation that produced it - never a separate commit. Serves
/// three distinct downstream readers, each claiming rows via `SELECT ... FOR UPDATE SKIP LOCKED`
/// on its own partial index: SHIP-2's carrier-call worker (`CarrierCallRequested`, unprocessed),
/// SHIP-3's RabbitMQ relay (`ShipmentDispatched`/`ShipmentCreationFailed`, unpublished), and the
/// Mongo read-model projector (every row, unprojected) - see contracts/README.md.
/// </summary>
public sealed class ShipmentOutboxEvent : IAuditable
{
    public Guid Id { get; private set; }

    /// <summary>Monotonic across the whole table (Postgres BIGSERIAL) - lets the Mongo projector apply out-of-order concurrent claims as last-writer-wins by sequence, not by wall-clock arrival.</summary>
    public long OutboxSeq { get; private set; }

    public ShipmentId ShipmentId { get; private set; }

    public ShipmentOutboxEventType MessageType { get; private set; }

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Set by SHIP-2's worker (for `CarrierCallRequested`) or SHIP-3's relay (for the two published event types) once that row's own job is done.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Set by the Mongo read-model projector once it has applied this row - independent of `ProcessedAt`, which tracks the *other* consumer's own job.</summary>
    public DateTimeOffset? ProjectedAt { get; private set; }

    /// <summary>Captured at outbox-write time so SHIP-3's relay can continue the originating request's W3C trace from its own background context, where `Activity.Current` would otherwise be meaningless.</summary>
    public string? TraceParent { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;

    private ShipmentOutboxEvent()
    {
    }

    public static ShipmentOutboxEvent Create(ShipmentId shipmentId, ShipmentOutboxEventType messageType, string payloadJson, DateTimeOffset occurredAt, string? traceParent)
    {
        return new ShipmentOutboxEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            MessageType = messageType,
            Payload = payloadJson,
            OccurredAt = occurredAt,
            TraceParent = traceParent
        };
    }

    public void MarkProcessed(DateTimeOffset processedAt) => ProcessedAt = processedAt;

    public void MarkProjected(DateTimeOffset projectedAt) => ProjectedAt = projectedAt;
}
