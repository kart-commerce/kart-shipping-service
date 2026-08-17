namespace Kart.Shipping.Infrastructure.Carriers;

/// <summary>Binds the "Carriers" configuration section. `Priority` is `CarrierSelectionPolicy` (ddd-model.md) - an externally-configured ordered list, primary then secondary on the primary's circuit tripping, never a computed cheapest/fastest rule.</summary>
public sealed class CarrierOptions
{
    public const string SectionName = "Carriers";

    public IReadOnlyList<string> Priority { get; set; } = [SimulatedPrimaryCarrierClient.Code, SimulatedSecondaryCarrierClient.Code];
}
