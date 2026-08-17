using Kart.Shipping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Shipping.Infrastructure.Persistence.Configurations;

public sealed class ShipmentIdempotencyKeyConfiguration : IEntityTypeConfiguration<ShipmentIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<ShipmentIdempotencyKey> builder)
    {
        builder.ToTable("shipment_idempotency_keys");

        builder.HasKey(k => k.IdempotencyKey);
        builder.Property(k => k.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256).ValueGeneratedNever();

        builder.Property(k => k.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(k => k.ShipmentId).HasColumnName("shipment_id").IsRequired();
        builder.Property(k => k.ResponseStatus).HasColumnName("response_status").IsRequired();

        builder.Property(k => k.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(k => k.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(k => k.CreatedBy).HasColumnName("created_by").HasMaxLength(128).IsRequired();
        builder.Property(k => k.UpdatedBy).HasColumnName("updated_by").HasMaxLength(128).IsRequired();
    }
}
