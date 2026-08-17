using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;
using Kart.Shared.Domain;
using MediatR;

namespace Kart.Shipping.Application.Features.GetShipment;

/// <summary>SHIP-5 - `GET /v1/shipments/{id}`, served from the Mongo read model (CQRS query side).</summary>
public sealed record GetShipmentQuery(Guid ShipmentId) : IRequest<Result<ShipmentView>>;

public sealed class GetShipmentQueryHandler(IShipmentReadRepository readRepository) : IRequestHandler<GetShipmentQuery, Result<ShipmentView>>
{
    public async Task<Result<ShipmentView>> Handle(GetShipmentQuery request, CancellationToken cancellationToken)
    {
        var view = await readRepository.GetByIdAsync(request.ShipmentId, cancellationToken);
        return view is null
            ? Result.Failure<ShipmentView>(Error.NotFound($"No shipment found with id '{request.ShipmentId}'."))
            : Result.Success(view);
    }
}
