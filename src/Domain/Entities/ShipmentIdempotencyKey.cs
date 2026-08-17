using Kart.Shipping.Domain.Common;

namespace Kart.Shipping.Domain.Entities;

/// <summary>
/// Backs the mandatory `Idempotency-Key` header contract on `POST /v1/shipments` (SHIP-6,
/// api-contract.yaml) - not specified by database-design.md, added here (see contracts/README.md).
/// A retried request with the same key + an identical body replays the same `ResponseStatus`
/// (202/409), with the response body re-derived fresh from the current `Shipment` row rather than
/// frozen at first-request time (the shipment's own state legitimately advances asynchronously;
/// the contract only requires the same outcome/status, not byte-frozen staleness). The same key
/// with a materially different body (`RequestHash` mismatch) is a `422`.
/// </summary>
public sealed class ShipmentIdempotencyKey : IAuditable
{
    public string IdempotencyKey { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public Guid ShipmentId { get; private set; }

    public int ResponseStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;

    private ShipmentIdempotencyKey()
    {
    }

    public static ShipmentIdempotencyKey Create(string idempotencyKey, string requestHash, Guid shipmentId, int responseStatus)
    {
        return new ShipmentIdempotencyKey
        {
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            ShipmentId = shipmentId,
            ResponseStatus = responseStatus
        };
    }
}
