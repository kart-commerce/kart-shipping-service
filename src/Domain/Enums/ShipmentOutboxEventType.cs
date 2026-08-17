namespace Kart.Shipping.Domain.Enums;

/// <summary>
/// The closed set of `shipment_outbox.message_type` values (database-design.md's CHECK
/// constraint). `CarrierCallRequested` is internal-only - it drives SHIP-2's carrier-call worker
/// and the Mongo read-model projector, but is never relayed to RabbitMQ (see contracts/README.md).
/// </summary>
public enum ShipmentOutboxEventType
{
    CarrierCallRequested,
    ShipmentDispatched,
    ShipmentCreationFailed
}
