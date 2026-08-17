using Kart.Shipping.Domain.Common;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Domain.Exceptions;
using Kart.Shipping.Domain.ValueObjects;

namespace Kart.Shipping.Domain.Entities;

/// <summary>
/// Aggregate root. The single authoritative record of one order's fulfillment attempt - carrier
/// selection through label generation. Created only by consuming `OrderConfirmed` (or, for SHIP-6,
/// the ops-only manual-create endpoint that reuses the exact same creation path); unique per
/// `OrderId` (at most one `Shipment` per `Order` - ddd-model.md).
/// </summary>
public sealed class Shipment : IAuditable
{
    public ShipmentId Id { get; private set; }

    public OrderId OrderId { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public Carrier? Carrier { get; private set; }

    public TrackingId? TrackingId { get; private set; }

    public FailureReason? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>EF Core materialization constructor.</summary>
    private Shipment()
    {
    }

    /// <summary>
    /// Creates a new shipment intent in `Pending`. Callers (SHIP-1's consumer handler and SHIP-6's
    /// manual-create handler) are responsible for the pre-call existence check on `OrderId` and for
    /// inserting the paired `CarrierCallRequested` outbox row in the same transaction -
    /// this factory only builds the aggregate itself.
    /// </summary>
    public static Shipment CreateIntent(OrderId orderId)
    {
        return new Shipment
        {
            Id = ShipmentId.New(),
            OrderId = orderId,
            Status = ShipmentStatus.Pending,
            Carrier = null,
            TrackingId = null,
            FailureReason = null
        };
    }

    /// <summary>
    /// Resolves the shipment as successfully dispatched. Both `carrier` and `trackingId` are
    /// mandatory - `ShipmentDispatched` must never be published without both durably set
    /// (ddd-model.md's uniqueness/publish invariant).
    /// </summary>
    public void MarkDispatched(Carrier carrier, TrackingId trackingId)
    {
        EnsurePending(nameof(ShipmentStatus.Dispatched));
        Status = ShipmentStatus.Dispatched;
        Carrier = carrier;
        TrackingId = trackingId;
    }

    /// <summary>Resolves the shipment as failed - every configured carrier option was exhausted (ADR-0015).</summary>
    public void MarkFailed(FailureReason reason)
    {
        EnsurePending(nameof(ShipmentStatus.Failed));
        Status = ShipmentStatus.Failed;
        FailureReason = reason;
    }

    private void EnsurePending(string attemptedStatus)
    {
        if (Status != ShipmentStatus.Pending)
        {
            throw new InvalidShipmentTransitionException(Id.Value, Status.ToString(), attemptedStatus);
        }
    }
}
