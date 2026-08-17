namespace Kart.Shipping.Domain.ValueObjects;

/// <summary>
/// Reference-only value object - `kart-order-service` owns the `Order` aggregate this points at.
/// This is also the natural, pre-existing business key `Shipment` is uniquely keyed on
/// (ddd-model.md Modeling Decision #1 - no separate IdempotencyRecord ledger is needed because of
/// this), so it participates in the aggregate's own `UNIQUE(order_id)` invariant, not a foreign
/// aggregate's identity.
/// </summary>
public readonly record struct OrderId(Guid Value) : ITypedEntityId<OrderId>
{
    public static OrderId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
