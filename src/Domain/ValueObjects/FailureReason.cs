namespace Kart.Shipping.Domain.ValueObjects;

/// <summary>Nullable reason a `Shipment` reaches `Failed` - set once all configured carrier options are exhausted (ADR-0015).</summary>
public readonly record struct FailureReason
{
    public string Value { get; }

    public FailureReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Failure reason must not be empty.", nameof(value));
        }

        Value = value;
    }

    public static FailureReason From(string value) => new(value);

    public override string ToString() => Value;
}
