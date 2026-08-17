using Kart.Shipping.Domain.Entities;
using Kart.Shipping.Domain.ValueObjects;
using Kart.Shipping.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Shipping.Infrastructure.Persistence.Configurations;

/// <summary>Maps `shipments` exactly per database-design.md - the CHECK constraints and the `trg_shipments_status_guard` trigger are added via raw SQL in the initial migration (EF Core's fluent API has no first-class support for either).</summary>
public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasConversion(TypedIdValueConverters.For<ShipmentId>()).ValueGeneratedNever();

        builder.Property(s => s.OrderId).HasColumnName("order_id").HasConversion(TypedIdValueConverters.For<OrderId>()).IsRequired();
        builder.HasIndex(s => s.OrderId).IsUnique();

        builder.Property(s => s.Status).HasColumnName("status").HasConversion(EnumDbValueConverters.ShipmentStatus).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Carrier).HasColumnName("carrier").HasConversion(DomainValueConverters.Carrier).IsRequired(false);
        builder.Property(s => s.TrackingId).HasColumnName("tracking_id").HasConversion(DomainValueConverters.TrackingId).IsRequired(false);
        builder.Property(s => s.FailureReason).HasColumnName("failure_reason").HasConversion(DomainValueConverters.FailureReason).IsRequired(false);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasMaxLength(128).IsRequired();
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").HasMaxLength(128).IsRequired();
    }
}
