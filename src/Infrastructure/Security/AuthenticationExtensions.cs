using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kart.Shipping.Infrastructure.Security;

/// <summary>api-contract.yaml's `opsPrincipal` scheme: an Identity-issued RS256 JWT carrying an OAuth2 `scope` claim, checked structurally here (signature + expiry) - never re-deriving role grants locally (BRD §24.1, Identity is the sole issuer of platform role/scope claims).</summary>
public static class AuthenticationExtensions
{
    public const string ShipmentsReadPolicy = "ShipmentsRead";
    public const string ShipmentsWritePolicy = "ShipmentsWrite";
    private const string ShipmentsReadScope = "shipments-read";
    private const string ShipmentsWriteScope = "shipments-write";

    public static IServiceCollection AddShippingAuthentication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient<JwksSigningKeyResolver>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksSigningKeyResolver>((options, resolver) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = resolver.ResolveSigningKeys
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(ShipmentsReadPolicy, policy => policy.RequireAssertion(ctx => HasScope(ctx, ShipmentsReadScope) || HasScope(ctx, ShipmentsWriteScope)))
            .AddPolicy(ShipmentsWritePolicy, policy => policy.RequireAssertion(ctx => HasScope(ctx, ShipmentsWriteScope)));

        return services;
    }

    private static bool HasScope(AuthorizationHandlerContext ctx, string scope) =>
        (ctx.User.FindFirst("scope")?.Value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope);
}
