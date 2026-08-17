using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kart.Shipping.Infrastructure.Auditing;

/// <summary>
/// Auto-stamps `CreatedAt`/`UpdatedAt` on every <see cref="IAuditable"/> entity at SaveChanges
/// time - the one piece of ddd-model.md's audit-actor invariant that's safe to apply generically
/// (a timestamp has no ambiguity). `CreatedBy`/`UpdatedBy` are deliberately NOT auto-filled here:
/// this interceptor has no reliable notion of "who is currently acting" that holds true across
/// both HTTP requests (an ops-principal's `sub` claim) and background workers/consumers (a
/// `system:*` sentinel) without inventing an ambient-context mechanism nothing else in this
/// service needs. Every mutation path sets its own actor explicitly instead (see
/// <see cref="Kart.Shipping.Application.Common.ShipmentCreationService"/> and the carrier-call
/// worker/outbox relay); this interceptor fails fast if one of them forgot to.
/// </summary>
public sealed class AuditableEntitySaveChangesInterceptor(IDateTimeProvider dateTimeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = dateTimeProvider.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                RequireActor(entry, entry.Entity.CreatedBy, nameof(IAuditable.CreatedBy));
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                RequireActor(entry, entry.Entity.UpdatedBy, nameof(IAuditable.UpdatedBy));
            }
        }
    }

    private static void RequireActor(EntityEntry<IAuditable> entry, string actor, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new InvalidOperationException(
                $"{entry.Entity.GetType().Name}.{propertyName} must be set to a resolved actor (a 'system:*' sentinel or a real principal id) before SaveChanges - ddd-model.md's audit-actor invariant forbids NULL/empty here.");
        }
    }
}
