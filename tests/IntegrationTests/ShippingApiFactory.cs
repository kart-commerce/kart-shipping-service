using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Kart.Shipping.IntegrationTests;

/// <summary>
/// Real Postgres + Mongo + RabbitMQ via Testcontainers - not swapped for Sqlite/in-memory fakes,
/// per the user's explicit "test against real dependencies" requirement. Runs this service's own
/// actual EF Core migrations against the ephemeral Postgres container, and applies the real
/// message-bus manifest against the ephemeral RabbitMQ container (including declaring the
/// external `order.exchange` - see `RabbitMqTopologyProvisioner`), so a test can publish a real
/// `OrderConfirmed` message and observe the full SHIP-1→SHIP-2→SHIP-3→read-model-projection
/// pipeline end-to-end. JWT validation is swapped for a fixed in-test RSA key (no HTTP call to a
/// real identity-service is needed to test this repo in isolation) via <see cref="IssueOpsToken"/>.
/// </summary>
public sealed class ShippingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kart_shipping_test")
        .Build();

    private readonly MongoDbContainer _mongo = new MongoDbBuilder()
        .WithImage("mongo:7.0")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .WithEnvironment("RABBITMQ_DEFAULT_USER", "shipping")
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", "shipping")
        .Build();

    private readonly RSA _testSigningKey = RSA.Create(2048);
    private string _globalConfigPath = string.Empty;

    public string RabbitMqHostName => _rabbitMq.Hostname;
    public int RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _mongo.StartAsync(), _rabbitMq.StartAsync());

        // AddKartGlobalConfig fails fast unless GlobalConfig:Path names a real file - an
        // (almost) empty one satisfies the contract without needing any real secret here, since
        // everything this test needs is instead supplied via plain environment variables below
        // (added to configuration BEFORE the GlobalConfig source, so they still take effect for
        // every key the minimal file itself doesn't set).
        _globalConfigPath = Path.Combine(Path.GetTempPath(), $"shipping-test-globalconfig-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_globalConfigPath, """{"Global":{},"Services":{"kart-shipping-service":{}}}""");

        Environment.SetEnvironmentVariable("GlobalConfig__Path", _globalConfigPath);
        Environment.SetEnvironmentVariable("ConnectionStrings__ShippingDatabase", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Mongo__ConnectionString", _mongo.GetConnectionString());
        Environment.SetEnvironmentVariable("Mongo__Database", "kart_shipping_read_test");
        Environment.SetEnvironmentVariable("RabbitMq__HostName", RabbitMqHostName);
        Environment.SetEnvironmentVariable("RabbitMq__Port", RabbitMqPort.ToString());
        Environment.SetEnvironmentVariable("RabbitMq__UserName", "shipping");
        Environment.SetEnvironmentVariable("RabbitMq__Password", "shipping");

        // Migrate via a standalone DbContext BEFORE the host (and, with it, every background
        // worker that queries `shipments`/`shipment_outbox`) starts. Discovered live: accessing
        // `Services` starts the host's hosted services immediately - migrating only afterwards
        // left a real window where SHIP-2/3/the read-model projector queried tables that did not
        // exist yet, and since .NET 6+ stops the ENTIRE HOST on an unhandled BackgroundService
        // exception by default, that race took the whole test host down instead of failing one
        // poll tick (see the matching fix in CarrierCallWorkerHostedService/
        // ReadModelProjectionHostedService, which now also tolerate this independently).
        var migrationOptions = new DbContextOptionsBuilder<Kart.Shipping.Infrastructure.Persistence.ShippingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var migrationContext = new Kart.Shipping.Infrastructure.Persistence.ShippingDbContext(migrationOptions))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // Forces host creation (Mongo index creation + RabbitMQ topology declaration) up front
        // rather than lazily on first use, now that the schema is guaranteed to already exist.
        _ = Services;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Swap the real JWKS-fetching resolver for a fixed in-test key - no live
            // identity-service needed to test this repo in isolation.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => [new RsaSecurityKey(_testSigningKey)];
            });
        });
    }

    /// <summary>Mints a locally-signed test token carrying the given scopes - stands in for an Identity-issued ops-principal access token.</summary>
    public string IssueOpsToken(params string[] scopes)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(new RsaSecurityKey(_testSigningKey), SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim("sub", "ops-tester"), new Claim("scope", string.Join(' ', scopes))],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return handler.WriteToken(token);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _mongo.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
        if (File.Exists(_globalConfigPath))
        {
            File.Delete(_globalConfigPath);
        }
    }
}
