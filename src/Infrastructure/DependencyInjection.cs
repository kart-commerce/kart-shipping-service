using Kart.Shared.Auditing;
using Kart.Shared.Messaging;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Infrastructure.Auditing;
using Kart.Shipping.Infrastructure.Carriers;
using Kart.Shipping.Infrastructure.Messaging;
using Kart.Shipping.Infrastructure.Persistence;
using Kart.Shipping.Infrastructure.Persistence.ReadModel;
using Kart.Shipping.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Kart.Shipping.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentPrincipal, CurrentPrincipal>();

        // --- Write-side persistence (PostgreSQL, source of truth) ---
        services.AddScoped<Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor, AuditableEntitySaveChangesInterceptor>();
        services.AddDbContext<ShippingDbContext>((sp, options) =>
            options.UseNpgsql(configuration.GetConnectionString("ShippingDatabase"))
                .AddInterceptors(sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor>()));
        services.AddScoped<IShippingDbContext>(sp => sp.GetRequiredService<ShippingDbContext>());

        // --- Audit logging (contracts/README.md deviation #5 - a real sink, not the shared package's NullAuditLogWriter) ---
        services.AddKartAuditing<EfCoreAuditLogWriter>();

        // --- Read-side persistence (MongoDB, CQRS query side - contracts/README.md deviation #1) ---
        services.AddOptions<MongoOptions>().Bind(configuration.GetSection("Mongo")).ValidateOnStart();
        services.AddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(options.Database);
        });
        services.AddSingleton<ShippingReadDbContext>();
        services.AddScoped<IShipmentReadRepository, ShipmentReadRepository>();
        services.AddScoped<ReadModelProjectionWriter>();
        services.AddHostedService<MongoIndexInitializerHostedService>();

        // --- Simulated carrier integration (contracts/README.md deviation #2) ---
        services.AddOptions<CarrierOptions>().Bind(configuration.GetSection(CarrierOptions.SectionName));
        services.AddSingleton<Application.Common.Interfaces.ICarrierClient, SimulatedPrimaryCarrierClient>();
        services.AddSingleton<Application.Common.Interfaces.ICarrierClient, SimulatedSecondaryCarrierClient>();
        services.AddSingleton<ICarrierDispatcher, CarrierDispatcher>();

        // --- Messaging: contracts/message-bus-manifest.json is the single source of truth for
        // this service's entire RabbitMQ topology - nothing messaging-related is hardcoded in C#.
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(o => string.IsNullOrEmpty(o.UserName) == string.IsNullOrEmpty(o.Password), "RabbitMq:UserName and RabbitMq:Password must either both be set or both be left unset.")
            .ValidateOnStart();
        services.AddKartMessageBusManifest(sp => sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value.ManifestPath);
        services.AddKartRabbitMqConnectionFactory(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            return new RabbitMqConnectionSettings(options.HostName, options.Port, options.UserName, options.Password);
        });
        services.AddKartRabbitMqTopologyStartup();
        services.AddHostedService<OrderConfirmedConsumerHostedService>();
        services.AddHostedService<CarrierCallWorkerHostedService>();
        services.AddHostedService<OutboxRelayHostedService>();
        services.AddHostedService<ReadModelProjectionHostedService>();

        // --- AuthN/AuthZ (validates Identity-issued ops-principal tokens) ---
        services.AddShippingAuthentication();

        return services;
    }
}
