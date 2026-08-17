using System.Reflection;
using FluentValidation;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Behaviours;
using Kart.Shipping.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Kart.Shipping.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
        });
        services.AddValidatorsFromAssembly(assembly);

        // Shared by SHIP-1's consumer handler and SHIP-6's manual-create handler
        // (tickets.md's explicit recommendation to factor this into one call both invoke).
        services.AddScoped<IShipmentCreationService, ShipmentCreationService>();

        return services;
    }
}
