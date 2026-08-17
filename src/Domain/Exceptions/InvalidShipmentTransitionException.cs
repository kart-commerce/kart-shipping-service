namespace Kart.Shipping.Domain.Exceptions;

/// <summary>
/// Thrown when code attempts to move a `Shipment` out of a terminal state (`Dispatched`/`Failed`).
/// In-process defense-in-depth alongside the DB-level `trg_shipments_status_guard` trigger - the
/// application layer is expected to check `Status == Pending` before calling a transition method
/// (so this should never actually throw on the happy path; SHIP-2's worker treats "0 rows
/// affected" as the primary signal and never even calls these methods on an already-terminal row).
/// </summary>
public sealed class InvalidShipmentTransitionException(Guid shipmentId, string currentStatus, string attemptedStatus)
    : Exception($"Shipment {shipmentId} is already terminal ({currentStatus}) and cannot transition to {attemptedStatus}.")
{
    public Guid ShipmentId { get; } = shipmentId;
}
