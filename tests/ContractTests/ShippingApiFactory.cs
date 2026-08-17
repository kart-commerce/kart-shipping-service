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

namespace Kart.Shipping.ContractTests;

/// <summary>
/// Near-identical to IntegrationTests/ShippingApiFactory.cs (duplicated, not shared, matching
/// kart-identity-service's own convention of a separate factory per test project) - real
/// Postgres/Mongo/RabbitMQ via Testcontainers, so ContractTests validates this service's actual
/// live HTTP responses, not a mocked stand-in.
/// </summary>
public sealed class ShippingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").WithDatabase("kart_shipping_contract_test").Build();
    private readonly MongoDbContainer _mongo = new MongoDbBuilder().WithImage("mongo:7.0").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .WithEnvironment("RABBITMQ_DEFAULT_USER", "shipping")
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", "shipping")
        .Build();

    private readonly RSA _testSigningKey = RSA.Create(2048);
    private string _globalConfigPath = string.Empty;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _mongo.StartAsync(), _rabbitMq.StartAsync());

        _globalConfigPath = Path.Combine(Path.GetTempPath(), $"shipping-contract-globalconfig-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_globalConfigPath, """{"Global":{},"Services":{"kart-shipping-service":{}}}""");

        Environment.SetEnvironmentVariable("GlobalConfig__Path", _globalConfigPath);
        Environment.SetEnvironmentVariable("ConnectionStrings__ShippingDatabase", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Mongo__ConnectionString", _mongo.GetConnectionString());
        Environment.SetEnvironmentVariable("Mongo__Database", "kart_shipping_read_contract_test");
        Environment.SetEnvironmentVariable("RabbitMq__HostName", _rabbitMq.Hostname);
        Environment.SetEnvironmentVariable("RabbitMq__Port", _rabbitMq.GetMappedPublicPort(5672).ToString());
        Environment.SetEnvironmentVariable("RabbitMq__UserName", "shipping");
        Environment.SetEnvironmentVariable("RabbitMq__Password", "shipping");

        var migrationOptions = new DbContextOptionsBuilder<Kart.Shipping.Infrastructure.Persistence.ShippingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var migrationContext = new Kart.Shipping.Infrastructure.Persistence.ShippingDbContext(migrationOptions))
        {
            await migrationContext.Database.MigrateAsync();
        }

        _ = Services;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => [new RsaSecurityKey(_testSigningKey)];
            });
        });
    }

    public string IssueOpsToken(params string[] scopes)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(new RsaSecurityKey(_testSigningKey), SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim("sub", "contract-tester"), new Claim("scope", string.Join(' ', scopes))],
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
