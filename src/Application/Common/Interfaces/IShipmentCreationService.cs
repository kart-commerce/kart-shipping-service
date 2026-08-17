using Kart.Shipping.Domain.ValueObjects;

namespace Kart.Shipping.Application.Common.Interfaces;

/// <summary>
/// The single creation code path SHIP-1 (consuming `OrderConfirmed`) and SHIP-6 (the ops-only
/// manual-create endpoint) both invoke - tickets.md explicitly calls for factoring this shared
/// logic into one internal call both entry points use, rather than duplicating the pre-carrier-call
/// existence check + insert.
/// </summary>
public interface IShipmentCreationService
{
    Task<ShipmentCreationOutcome> CreateAsync(OrderId orderId, Address address, string actor, CancellationToken cancellationToken);
}

/// <summary>`AlreadyExisted = true` covers both a genuine duplicate/redelivered request and the race backstopped by the `UNIQUE(order_id)` constraint - both are treated identically, as a no-op.</summary>
public sealed record ShipmentCreationOutcome(ShipmentId ShipmentId, bool AlreadyExisted);
