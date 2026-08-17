using FluentValidation;
using Kart.Shipping.Application.Common.Interfaces;
using MediatR;

namespace Kart.Shipping.Application.Features.ListShipments;

/// <summary>SHIP-4 - `GET /v1/shipments`, ops triage of stuck/Failed shipments, served from the Mongo read model.</summary>
public sealed record ListShipmentsQuery(string? OrderId, string? Status, string? Carrier, string? Cursor, int Limit) : IRequest<ShipmentPage>;

public sealed class ListShipmentsQueryHandler(IShipmentReadRepository readRepository) : IRequestHandler<ListShipmentsQuery, ShipmentPage>
{
    public Task<ShipmentPage> Handle(ListShipmentsQuery request, CancellationToken cancellationToken) =>
        readRepository.ListAsync(new ShipmentListFilter(request.OrderId, request.Status, request.Carrier, request.Cursor, request.Limit), cancellationToken);
}

public sealed class ListShipmentsQueryValidator : AbstractValidator<ListShipmentsQuery>
{
    private static readonly string[] ValidStatuses = ["Pending", "Dispatched", "Failed"];

    public ListShipmentsQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).Must(s => s is null || ValidStatuses.Contains(s)).WithMessage($"status must be one of: {string.Join(", ", ValidStatuses)}");
    }
}
