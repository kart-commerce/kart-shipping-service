using Kart.Shipping.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Shipping.Infrastructure.Persistence.Configurations;

public sealed class AuditLogRecordConfiguration : IEntityTypeConfiguration<AuditLogRecord>
{
    public void Configure(EntityTypeBuilder<AuditLogRecord> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.EntryId);
        builder.Property(a => a.EntryId).HasColumnName("entry_id").ValueGeneratedNever();

        builder.Property(a => a.ServiceName).HasColumnName("service_name").HasMaxLength(128).IsRequired();
        builder.Property(a => a.ActorId).HasColumnName("actor_id").HasMaxLength(128).IsRequired();
        builder.Property(a => a.ActorType).HasColumnName("actor_type").HasMaxLength(32).IsRequired();
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id").HasMaxLength(128).IsRequired();
        builder.Property(a => a.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(a => a.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb").IsRequired(false);

        builder.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("idx_audit_log_entity");
    }
}
