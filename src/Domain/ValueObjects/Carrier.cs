namespace Kart.Shipping.Domain.ValueObjects;

/// <summary>
/// The carrier that actually produced a valid label - set exactly once, by the succeeding
/// attempt, never speculatively (ddd-model.md). Deliberately a validated string, not a fixed
/// enum: `CarrierSelectionPolicy` is an externally-configured ordered priority list, not a closed
/// set baked into the domain model (Modeling Decision #2).
/// </summary>
public readonly record struct Carrier
{
    public string Value { get; }

    public Carrier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Carrier code must not be empty.", nameof(value));
        }

        Value = value;
    }

    public static Carrier From(string value) => new(value);

    public override string ToString() => Value;
}
