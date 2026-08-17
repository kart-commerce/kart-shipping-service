namespace Kart.Shipping.Domain.Enums;

/// <summary>
/// `Pending` is the only non-terminal value, entered the instant the shipment-intent row commits.
/// `Dispatched`/`Failed` are both terminal - legal transitions are exactly
/// `Pending -> {Dispatched, Failed}`, once, never reversed (ddd-model.md).
/// </summary>
public enum ShipmentStatus
{
    Pending,
    Dispatched,
    Failed
}
