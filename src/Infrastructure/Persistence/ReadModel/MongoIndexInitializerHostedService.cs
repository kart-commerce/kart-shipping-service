using Kart.Shipping.Infrastructure.Persistence.ReadModel.Documents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Kart.Shipping.Infrastructure.Persistence.ReadModel;

/// <summary>Declares every index the read side's query shapes need, once at startup - idempotent, fire-and-forget (a Mongo outage at boot must not block Kestrel from starting). Mirrors kart-payment-service's identically-shaped initializer.</summary>
public sealed class MongoIndexInitializerHostedService(ShippingReadDbContext context, ILogger<MongoIndexInitializerHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = DeclareIndexesAsync(cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Exposed (not private) so tests can await index creation deterministically instead of racing the fire-and-forget call in <see cref="StartAsync"/>.</summary>
    public async Task DeclareIndexesAsync(CancellationToken cancellationToken)
    {
        await CreateIndexAsync("shipment_read.orderId", () =>
            context.Shipments.Indexes.CreateOneAsync(
                new CreateIndexModel<ShipmentReadDocument>(
                    Builders<ShipmentReadDocument>.IndexKeys.Ascending(d => d.OrderId),
                    new CreateIndexOptions { Unique = true }),
                cancellationToken: cancellationToken));

        // ListShipments' (SHIP-4) status/carrier filters + cursor pagination shape.
        await CreateIndexAsync("shipment_read.status+createdAt", () =>
            context.Shipments.Indexes.CreateOneAsync(
                new CreateIndexModel<ShipmentReadDocument>(
                    Builders<ShipmentReadDocument>.IndexKeys.Ascending(d => d.Status).Ascending(d => d.CreatedAt)),
                cancellationToken: cancellationToken));

        await CreateIndexAsync("shipment_read.carrier", () =>
            context.Shipments.Indexes.CreateOneAsync(
                new CreateIndexModel<ShipmentReadDocument>(Builders<ShipmentReadDocument>.IndexKeys.Ascending(d => d.Carrier)),
                cancellationToken: cancellationToken));
    }

    private async Task CreateIndexAsync(string description, Func<Task<string>> createIndex)
    {
        try
        {
            await createIndex();
            logger.LogInformation("Declared MongoDB read-model index: {Description}.", description);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not declare MongoDB read-model index '{Description}' at startup.", description);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
