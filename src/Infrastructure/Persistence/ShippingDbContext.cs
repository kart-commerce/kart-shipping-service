using System.Reflection;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Domain.Entities;
using Kart.Shipping.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Kart.Shipping.Infrastructure.Persistence;

public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options), IShippingDbContext
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<ShipmentOutboxEvent> ShipmentOutboxEvents => Set<ShipmentOutboxEvent>();

    public DbSet<ShipmentIdempotencyKey> IdempotencyKeys => Set<ShipmentIdempotencyKey>();

    public DbSet<AuditLogRecord> AuditLog => Set<AuditLogRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
