using Kart.Shipping.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kart.Shipping.Application.Common.Interfaces;

/// <summary>Application-layer seam over the write-side EF Core context - lets handlers stay ignorant of Npgsql/EF Core package details, and lets UnitTests substitute an InMemory-backed implementation.</summary>
public interface IShippingDbContext
{
    DbSet<Shipment> Shipments { get; }

    DbSet<ShipmentOutboxEvent> ShipmentOutboxEvents { get; }

    DbSet<ShipmentIdempotencyKey> IdempotencyKeys { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
