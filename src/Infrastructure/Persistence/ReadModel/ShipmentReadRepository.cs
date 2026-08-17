using System.Text;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Infrastructure.Persistence.ReadModel.Documents;
using MongoDB.Driver;

namespace Kart.Shipping.Infrastructure.Persistence.ReadModel;

/// <summary>The CQRS query side of `GetShipment`/`ListShipments` (SHIP-4/SHIP-5) - reads exclusively from MongoDB, never PostgreSQL.</summary>
public sealed class ShipmentReadRepository(ShippingReadDbContext context) : IShipmentReadRepository
{
    public async Task<ShipmentView?> GetByIdAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var document = await context.Shipments.Find(d => d.Id == shipmentId).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : ToView(document);
    }

    public async Task<ShipmentPage> ListAsync(ShipmentListFilter filter, CancellationToken cancellationToken)
    {
        var builder = Builders<ShipmentReadDocument>.Filter;
        var filters = new List<FilterDefinition<ShipmentReadDocument>>();

        if (!string.IsNullOrEmpty(filter.OrderId)) filters.Add(builder.Eq(d => d.OrderId, filter.OrderId));
        if (!string.IsNullOrEmpty(filter.Status)) filters.Add(builder.Eq(d => d.Status, filter.Status));
        if (!string.IsNullOrEmpty(filter.Carrier)) filters.Add(builder.Eq(d => d.Carrier, filter.Carrier));

        if (!string.IsNullOrEmpty(filter.Cursor))
        {
            var (afterCreatedAt, afterId) = DecodeCursor(filter.Cursor);
            filters.Add(builder.Or(
                builder.Gt(d => d.CreatedAt, afterCreatedAt),
                builder.And(builder.Eq(d => d.CreatedAt, afterCreatedAt), builder.Gt(d => d.Id, afterId))));
        }

        var combined = filters.Count > 0 ? builder.And(filters) : builder.Empty;

        var documents = await context.Shipments.Find(combined)
            .SortBy(d => d.CreatedAt).ThenBy(d => d.Id)
            .Limit(filter.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = documents.Count > filter.Limit;
        var page = hasMore ? documents.Take(filter.Limit).ToList() : documents;
        var nextCursor = hasMore ? EncodeCursor(page[^1].CreatedAt, page[^1].Id) : null;

        return new ShipmentPage(page.Select(ToView).ToList(), nextCursor);
    }

    private static ShipmentView ToView(ShipmentReadDocument d) => new(
        d.Id,
        d.OrderId,
        d.Status,
        d.Carrier,
        d.TrackingId,
        d.FailureReason,
        DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc),
        d.DispatchedAt.HasValue ? DateTime.SpecifyKind(d.DispatchedAt.Value, DateTimeKind.Utc) : null,
        d.FailedAt.HasValue ? DateTime.SpecifyKind(d.FailedAt.Value, DateTimeKind.Utc) : null);

    private static string EncodeCursor(DateTime createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt:O}|{id}"));

    private static (DateTime CreatedAt, Guid Id) DecodeCursor(string cursor)
    {
        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
        return (DateTime.Parse(parts[0]).ToUniversalTime(), Guid.Parse(parts[1]));
    }
}
