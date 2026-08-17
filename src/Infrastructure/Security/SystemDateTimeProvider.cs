using Kart.Shipping.Application.Common.Interfaces;

namespace Kart.Shipping.Infrastructure.Security;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
