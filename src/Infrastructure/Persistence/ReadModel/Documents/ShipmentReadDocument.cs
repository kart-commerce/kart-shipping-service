using MongoDB.Bson.Serialization.Attributes;

namespace Kart.Shipping.Infrastructure.Persistence.ReadModel.Documents;

/// <summary>
/// CQRS read-side, denormalized copy of a `shipments` row - `_id = shipmentId`. Kept in sync from
/// PostgreSQL via <see cref="Messaging.ReadModelProjectionHostedService"/>, never written by any
/// request handler directly (contracts/README.md deviation #1). This is what `GetShipment`/
/// `ListShipments` (SHIP-4/SHIP-5) actually read - the user's explicit CQRS requirement.
/// Sharded on `{_id: "hashed"}` in production.
/// </summary>
public sealed class ShipmentReadDocument
{
    [BsonId]
    public Guid Id { get; set; }

    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("carrier")]
    public string? Carrier { get; set; }

    [BsonElement("trackingId")]
    public string? TrackingId { get; set; }

    [BsonElement("failureReason")]
    public string? FailureReason { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("dispatchedAt")]
    public DateTime? DispatchedAt { get; set; }

    [BsonElement("failedAt")]
    public DateTime? FailedAt { get; set; }

    /// <summary>Race-safety guard (contracts/README.md) - an upsert only applies if `outbox_seq > LastAppliedSeq`, so two horizontally-scaled projector instances claiming out-of-order rows for the same shipment converge on the highest-sequence event regardless of arrival order.</summary>
    [BsonElement("lastAppliedSeq")]
    public long LastAppliedSeq { get; set; }
}
