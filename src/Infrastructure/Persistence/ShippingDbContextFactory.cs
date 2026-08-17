using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kart.Shipping.Infrastructure.Persistence;

/// <summary>
/// Design-time entry point `dotnet ef` uses (per service-infra-rollout-checklist.md) - reads
/// `SHIPPING_DB_CONNECTION_STRING` so `dotnet ef migrations add/database update` never needs the
/// full Api host (JWT signing, RabbitMQ, Mongo, etc.) to run. Same convention as
/// kart-identity-service's `IdentityDbContextFactory`.
/// </summary>
public sealed class ShippingDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SHIPPING_DB_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=kart_shipping_dev;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ShippingDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ShippingDbContext(optionsBuilder.Options);
    }
}
