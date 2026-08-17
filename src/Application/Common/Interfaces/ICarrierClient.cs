using Kart.Shipping.Domain.ValueObjects;

namespace Kart.Shipping.Application.Common.Interfaces;

/// <summary>
/// A single carrier integration - label generation abstraction. `SimulatedPrimaryCarrierClient`/
/// `SimulatedSecondaryCarrierClient` (Infrastructure/Carriers) are the only implementations today
/// (contracts/README.md deviation #2); a real carrier SDK swaps in behind this same interface with
/// zero change to Application/Domain.
/// </summary>
public interface ICarrierClient
{
    /// <summary>Stable identifier used both as the configured `CarrierPriority` key and, on success, as the persisted `Carrier` value object.</summary>
    string CarrierCode { get; }

    Task<CarrierLabelResult> RequestLabelAsync(Address address, CancellationToken cancellationToken);
}

public sealed record CarrierLabelResult(bool AddressRejected, TrackingId? TrackingId);
