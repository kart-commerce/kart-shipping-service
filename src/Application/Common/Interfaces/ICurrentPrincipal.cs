namespace Kart.Shipping.Application.Common.Interfaces;

/// <summary>Resolves the acting principal id for audit stamping on request-path mutations (SHIP-6). Background workers/consumers use the `system:*` sentinels in <see cref="SystemPrincipals"/> directly instead of this.</summary>
public interface ICurrentPrincipal
{
    string? Subject { get; }
}
