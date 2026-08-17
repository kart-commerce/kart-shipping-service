using System.Text.Json;
using Kart.Shared.Auditing;
using Kart.Shipping.Infrastructure.Persistence;

namespace Kart.Shipping.Infrastructure.Auditing;

/// <summary>
/// The real `IAuditLogWriter` this service wires (`AddKartAuditing&lt;EfCoreAuditLogWriter&gt;()`)
/// instead of `Kart.Shared.Auditing`'s default `NullAuditLogWriter` - no other service on the
/// platform has completed this contract with a real sink yet (contracts/README.md deviation #5).
/// Commits independently of the handler's own SaveChanges - the audit trail is an observability/
/// compliance record, not a participant in the business transaction's atomicity.
/// </summary>
public sealed class EfCoreAuditLogWriter(ShippingDbContext dbContext) : IAuditLogWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        dbContext.AuditLog.Add(new AuditLogRecord
        {
            EntryId = entry.EntryId,
            ServiceName = entry.ServiceName,
            ActorId = entry.ActorId,
            ActorType = entry.ActorType,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OccurredAt = entry.OccurredAt,
            MetadataJson = entry.Metadata is null ? null : JsonSerializer.Serialize(entry.Metadata, SerializerOptions)
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
