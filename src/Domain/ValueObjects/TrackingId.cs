namespace Kart.Shipping.Domain.ValueObjects;

/// <summary>
/// Carrier-issued tracking identifier - nullable until `Dispatched`. Glossary ownership of the
/// *term* TrackingId stays with kart-delivery-tracking-service (ddd-model.md Modeling Decision
/// #5) even though this service is where the value is first captured.
/// </summary>
public readonly record struct TrackingId
{
    public string Value { get; }

    public TrackingId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tracking id must not be empty.", nameof(value));
        }

        Value = value;
    }

    public static TrackingId From(string value) => new(value);

    public override string ToString() => Value;
}
