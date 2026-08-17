namespace Kart.Shipping.Domain.Common;

/// <summary>
/// BRD §24.3 audit-actor invariant: every mutable row stamps created_at/updated_at/created_by/
/// updated_by, never NULL. Values are assigned exclusively by
/// Infrastructure.Auditing.AuditableEntitySaveChangesInterceptor at SaveChanges time - domain code
/// never sets these directly (ddd-model.md's "system:shipping-*" sentinel actors are resolved
/// there, not here).
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }

    string CreatedBy { get; set; }

    string UpdatedBy { get; set; }
}
