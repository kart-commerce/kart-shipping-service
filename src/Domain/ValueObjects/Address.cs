namespace Kart.Shipping.Domain.ValueObjects;

/// <summary>
/// The destination address carried on `OrderConfirmed`. Deliberately never persisted as a column
/// on `shipments` (database-design.md's PII-minimization decision) - it flows only transiently
/// into the `CarrierCallRequested` outbox row's JSON payload, and into the carrier-call attempt
/// itself. `PostalCode` doubles as this build's simulated-carrier scenario selector (see
/// Infrastructure/Carriers) since no real carrier account exists to validate a real address against.
/// </summary>
public sealed record Address(string Line1, string? Line2, string City, string? State, string PostalCode, string Country)
{
    public static Address Create(string line1, string? line2, string city, string? state, string postalCode, string country)
    {
        if (string.IsNullOrWhiteSpace(line1)) throw new ArgumentException("Address line1 is required.", nameof(line1));
        if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("Address city is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(postalCode)) throw new ArgumentException("Address postalCode is required.", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(country)) throw new ArgumentException("Address country is required.", nameof(country));

        return new Address(line1, line2, city, state, postalCode, country);
    }
}
