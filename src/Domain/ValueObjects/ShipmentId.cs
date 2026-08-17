namespace Kart.Shipping.Domain.ValueObjects;

/// <summary>Identity of the `Shipment` aggregate root - the URL-facing id in `GET /v1/shipments/{id}`.</summary>
public readonly record struct ShipmentId(Guid Value) : ITypedEntityId<ShipmentId>
{
    public static ShipmentId New() => new(Guid.NewGuid());

    public static ShipmentId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
