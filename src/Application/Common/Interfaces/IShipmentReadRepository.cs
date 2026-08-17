using Kart.Shipping.Application.Common.Models;

namespace Kart.Shipping.Application.Common.Interfaces;

/// <summary>The CQRS query side (SHIP-4/SHIP-5) - reads exclusively from the MongoDB read model, never PostgreSQL.</summary>
public interface IShipmentReadRepository
{
    Task<ShipmentView?> GetByIdAsync(Guid shipmentId, CancellationToken cancellationToken);

    Task<ShipmentPage> ListAsync(ShipmentListFilter filter, CancellationToken cancellationToken);
}

public sealed record ShipmentListFilter(string? OrderId, string? Status, string? Carrier, string? Cursor, int Limit);

public sealed record ShipmentPage(IReadOnlyList<ShipmentView> Items, string? NextCursor);
