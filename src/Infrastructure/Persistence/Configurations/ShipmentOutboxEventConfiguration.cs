using Kart.Shipping.Domain.Entities;
using Kart.Shipping.Domain.ValueObjects;
using Kart.Shipping.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Shipping.Infrastructure.Persistence.Configurations;

/// <summary>Maps `shipment_outbox` per database-design.md, plus the `outbox_seq`/`projected_at` additions documented in contracts/README.md.</summary>
public sealed class ShipmentOutboxEventConfiguration : IEntityTypeConfiguration<ShipmentOutboxEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentOutboxEvent> builder)
    {
        builder.ToTable("shipment_outbox");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.OutboxSeq).HasColumnName("outbox_seq").ValueGeneratedOnAdd().UseIdentityAlwaysColumn();

        builder.Property(e => e.ShipmentId).HasColumnName("shipment_id").HasConversion(TypedIdValueConverters.For<ShipmentId>()).IsRequired();
        builder.Property(e => e.MessageType).HasColumnName("message_type").HasConversion(EnumDbValueConverters.ShipmentOutboxEventType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at").IsRequired(false);
        builder.Property(e => e.ProjectedAt).HasColumnName("projected_at").IsRequired(false);
        builder.Property(e => e.TraceParent).HasColumnName("trace_parent").HasMaxLength(256).IsRequired(false);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(128).IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(128).IsRequired();

        builder.HasIndex(e => e.ShipmentId).HasDatabaseName("idx_shipment_outbox_shipment");

        // Partial indexes (database-design.md) are declared via raw SQL in the initial migration -
        // EF Core's HasFilter works per-provider but the three distinct partial predicates here
        // are clearer expressed directly as the exact SQL the design doc specifies.
    }
}
