using Kart.Shipping.Infrastructure.Auditing;
using Kart.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kart.Shipping.UnitTests.TestSupport;

/// <summary>Fresh EF Core InMemory-backed `ShippingDbContext` per test, with the same audit-stamping interceptor production uses (so a test that forgets to set an actor fails the same way production would) - mirrors kart-identity-service's own unit-test pattern of using the concrete DbContext directly against `UseInMemoryDatabase`.</summary>
public static class InMemoryDbContextFactory
{
    public static ShippingDbContext Create(DateTimeOffset now)
    {
        var options = new DbContextOptionsBuilder<ShippingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(new FixedDateTimeProvider(now)))
            .Options;

        return new ShippingDbContext(options);
    }
}
