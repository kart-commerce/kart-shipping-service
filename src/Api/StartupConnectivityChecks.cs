using Kart.Shipping.Infrastructure.Persistence.ReadModel;
using Kart.Shipping.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace Kart.Shipping.Api;

/// <summary>Verifies every infra dependency is reachable right after boot, one Connecting/connected log pair per dependency, so a misconfigured or unreachable Postgres/Mongo/RabbitMQ shows up immediately in the startup log instead of surfacing later as the first message's failure.</summary>
public static class StartupConnectivityChecks
{
    public static async Task RunAsync(WebApplication app)
    {
        // WebApplicationFactory-based tests (Contract/Integration) run this same Program.cs
        // against real Testcontainers-backed Postgres/Mongo/RabbitMQ, so this step still runs
        // there - only truly no-op for a hypothetical future unit-style factory that marks itself
        // "Testing" without providing any of these dependencies.
        if (app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        var logger = app.Logger;

        await CheckAsync(logger, "PostgresDB", async () =>
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            await dbContext.Database.CanConnectAsync();
        });

        await CheckAsync(logger, "MongoDB", async () =>
        {
            using var scope = app.Services.CreateScope();
            var readContext = scope.ServiceProvider.GetRequiredService<ShippingReadDbContext>();
            await readContext.Database.RunCommandAsync((MongoDB.Driver.Command<MongoDB.Bson.BsonDocument>)"{ping:1}");
        });

        await CheckAsync(logger, "RabbitMQ", () =>
        {
            var connectionFactory = app.Services.GetRequiredService<IConnectionFactory>();
            using var connection = connectionFactory.CreateConnection();
            return Task.CompletedTask;
        });
    }

    private static async Task CheckAsync(ILogger logger, string dependency, Func<Task> connect)
    {
        logger.LogInformation("Connecting Shipping {Dependency} ...", dependency);
        try
        {
            await connect();
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Failed to connect to Shipping {Dependency}", dependency);
            throw;
        }

        logger.LogInformation("{Dependency} connected", dependency);
    }
}
