using Kart.Shipping.Application.Common.Interfaces;

namespace Kart.Shipping.UnitTests.TestSupport;

public sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}
