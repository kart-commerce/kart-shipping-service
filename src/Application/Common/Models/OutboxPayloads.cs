namespace Kart.Shipping.Application.Common.Models;

/// <summary>`shipment_outbox.payload` shapes, exactly as event-contract.md/database-design.md specify. Internal-only - never relayed to RabbitMQ (see contracts/README.md).</summary>
public sealed record CarrierCallRequestedPayload(string OrderId, AddressDto Address);

/// <summary>`ShipmentDispatched`'s published payload.</summary>
public sealed record ShipmentDispatchedPayload(string OrderId, string Carrier, string TrackingId);

/// <summary>`ShipmentCreationFailed`'s published payload.</summary>
public sealed record ShipmentCreationFailedPayload(string OrderId, string Reason);
