using Kart.Shipping.Infrastructure.Persistence.ReadModel.Documents;
using MongoDB.Driver;

namespace Kart.Shipping.Infrastructure.Persistence.ReadModel;

/// <summary>Typed accessor for this service's denormalized MongoDB read collection - the CQRS query side. Deployed against a sharded MongoDB cluster in production; nothing here assumes a single node.</summary>
public sealed class ShippingReadDbContext(IMongoDatabase database)
{
    public const string ShipmentsCollectionName = "shipment_read";

    public IMongoDatabase Database { get; } = database;

    public IMongoCollection<ShipmentReadDocument> Shipments => Database.GetCollection<ShipmentReadDocument>(ShipmentsCollectionName);
}
