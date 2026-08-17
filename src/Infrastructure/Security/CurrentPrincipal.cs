using Kart.Shipping.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Kart.Shipping.Infrastructure.Security;

/// <summary>Resolves the ops-principal's `sub` claim for audit stamping on SHIP-6's request path. `MapInboundClaims = false` is set in <see cref="Kart.Shipping.Infrastructure.DependencyInjection"/>, so the raw "sub" claim type is read directly (mirrors kart-identity-service/kart-payment-service).</summary>
public sealed class CurrentPrincipal(IHttpContextAccessor httpContextAccessor) : ICurrentPrincipal
{
    public string? Subject => httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
}
