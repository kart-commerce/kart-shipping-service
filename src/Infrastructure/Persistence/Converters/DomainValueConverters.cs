using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kart.Shipping.Infrastructure.Persistence.Converters;

/// <summary>String-backed value objects don't share a common interface (unlike the Guid-backed typed IDs), so each gets its own explicit converter here, mirroring kart-identity-service's `DomainValueConverters`.</summary>
internal static class DomainValueConverters
{
    public static readonly ValueConverter<Carrier, string> Carrier = new(c => c.Value, v => new Carrier(v));

    public static readonly ValueConverter<TrackingId, string> TrackingId = new(t => t.Value, v => new TrackingId(v));

    public static readonly ValueConverter<FailureReason, string> FailureReason = new(f => f.Value, v => new FailureReason(v));
}

/// <summary>Enum -&gt; snake_case-ish PascalCase string mapping matching `database-design.md`'s CHECK-constraint vocabulary exactly (`'Pending'`, `'Dispatched'`, `'Failed'`, `'CarrierCallRequested'`, ...).</summary>
internal static class EnumDbValueConverters
{
    public static readonly ValueConverter<ShipmentStatus, string> ShipmentStatus = new(
        s => s.ToString(),
        v => Enum.Parse<ShipmentStatus>(v));

    public static readonly ValueConverter<ShipmentOutboxEventType, string> ShipmentOutboxEventType = new(
        s => s.ToString(),
        v => Enum.Parse<ShipmentOutboxEventType>(v));
}
