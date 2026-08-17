using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Domain.ValueObjects;

namespace Kart.Shipping.Infrastructure.Carriers;

/// <summary>
/// contracts/README.md deviation #2: no real carrier account exists, so both carriers are
/// deterministic simulators keyed on <see cref="Address.PostalCode"/>, documented here as the
/// single source of truth for every test scenario:
///   - "00000" -&gt; every carrier rejects the address (simulates "no carrier services this
///     destination" / address-validation failure - ADR-0015's `ShipmentCreationFailed` trigger).
///   - "11111" -&gt; Primary times out (trips its circuit), Secondary succeeds - the fallback path.
///   - "99999" -&gt; every carrier times out - both exhausted, `ShipmentCreationFailed`.
///   - anything else -&gt; Primary succeeds immediately.
/// A real carrier SDK (Shippo/EasyPost/etc.) swaps in behind <see cref="ICarrierClient"/> with zero
/// change to Application/Domain.
/// </summary>
public sealed class SimulatedPrimaryCarrierClient : ICarrierClient
{
    public const string Code = "Primary";

    public string CarrierCode => Code;

    public async Task<CarrierLabelResult> RequestLabelAsync(Address address, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);

        return address.PostalCode switch
        {
            "00000" => new CarrierLabelResult(AddressRejected: true, TrackingId: null),
            "11111" or "99999" => throw new SimulatedCarrierTimeoutException(Code),
            _ => new CarrierLabelResult(AddressRejected: false, TrackingId.From($"SIM-PRI-{Guid.NewGuid():N}"[..16].ToUpperInvariant()))
        };
    }
}

public sealed class SimulatedSecondaryCarrierClient : ICarrierClient
{
    public const string Code = "Secondary";

    public string CarrierCode => Code;

    public async Task<CarrierLabelResult> RequestLabelAsync(Address address, CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);

        return address.PostalCode switch
        {
            "00000" => new CarrierLabelResult(AddressRejected: true, TrackingId: null),
            "99999" => throw new SimulatedCarrierTimeoutException(Code),
            _ => new CarrierLabelResult(AddressRejected: false, TrackingId.From($"SIM-SEC-{Guid.NewGuid():N}"[..16].ToUpperInvariant()))
        };
    }
}
