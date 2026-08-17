using Kart.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kart.Shipping.Api.HealthChecks;

/// <summary>Readiness signal for the k8s Helm chart's `/health/ready` probe - a database that is reachable but behind on migrations must fail readiness too, not just an unreachable one, so a pod never accepts `OrderConfirmed` traffic before its own schema exists.</summary>
public sealed class ShippingDbHealthCheck(ShippingDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            return pending.Length == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{pending.Length} pending migration(s): {string.Join(", ", pending)}");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Shipping database is unreachable", exception);
        }
    }
}
