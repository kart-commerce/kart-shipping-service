using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Domain.ValueObjects;
using Kart.Shared.Auditing;
using MediatR;

namespace Kart.Shipping.Application.Features.CreateShipmentOnOrderConfirmed;

/// <summary>SHIP-1 - triggered by `OrderConfirmedConsumerHostedService` for every `OrderConfirmed` delivery.</summary>
public sealed record CreateShipmentOnOrderConfirmedCommand(string OrderId, AddressDto Address) : IRequest;

public sealed class CreateShipmentOnOrderConfirmedCommandHandler(IShipmentCreationService creationService, IAuditLogWriter auditLogWriter) : IRequestHandler<CreateShipmentOnOrderConfirmedCommand>
{
    public async Task Handle(CreateShipmentOnOrderConfirmedCommand request, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(Guid.Parse(request.OrderId));
        var address = Domain.ValueObjects.Address.Create(request.Address.Line1, request.Address.Line2, request.Address.City, request.Address.State, request.Address.PostalCode, request.Address.Country);

        var outcome = await creationService.CreateAsync(orderId, address, SystemPrincipals.OrderConfirmedConsumer, cancellationToken);

        if (!outcome.AlreadyExisted)
        {
            await auditLogWriter.WriteAsync(
                AuditLogEntry.Create("kart-shipping-service", SystemPrincipals.OrderConfirmedConsumer, "system", "ShipmentCreated", "Shipment", outcome.ShipmentId.ToString(), new Dictionary<string, object?> { ["orderId"] = request.OrderId }),
                cancellationToken);
        }
    }
}
