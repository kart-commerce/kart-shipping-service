namespace Kart.Shipping.Infrastructure.Auditing;

/// <summary>EF persistence model for the `audit_log` table - the real, queryable audit trail `EfCoreAuditLogWriter` writes `Kart.Shared.Auditing.AuditLogEntry` values into (see contracts/README.md deviation #5).</summary>
public sealed class AuditLogRecord
{
    public Guid EntryId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string ActorType { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string? MetadataJson { get; set; }
}
