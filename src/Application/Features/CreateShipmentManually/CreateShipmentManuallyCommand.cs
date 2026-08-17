using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Domain.Entities;
using Kart.Shipping.Domain.ValueObjects;
using Kart.Shared.Auditing;
using Kart.Shared.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kart.Shipping.Application.Features.CreateShipmentManually;

/// <summary>SHIP-6 - ops-only `POST /v1/shipments`, for the narrow case an `OrderConfirmed` was never consumed for a genuinely-confirmed order. Shares SHIP-1's exact creation path.</summary>
public sealed record CreateShipmentManuallyCommand(string IdempotencyKey, string OrderId, AddressDto Address, string ActorId) : IRequest<Result<CreateShipmentManuallyResponse>>;

/// <summary>`StatusCode` is 202 for a freshly (or idempotently replayed) accepted intent, 409 when this `orderId` already has a shipment under a *different* idempotency key.</summary>
public sealed record CreateShipmentManuallyResponse(int StatusCode, ShipmentView View);

public sealed class CreateShipmentManuallyCommandValidator : AbstractValidator<CreateShipmentManuallyCommand>
{
    public CreateShipmentManuallyCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty().Must(id => Guid.TryParse(id, out _)).WithMessage("orderId must be a valid guid.");
        RuleFor(x => x.Address).NotNull();
        RuleFor(x => x.Address.Line1).NotEmpty().When(x => x.Address is not null);
        RuleFor(x => x.Address.City).NotEmpty().When(x => x.Address is not null);
        RuleFor(x => x.Address.PostalCode).NotEmpty().When(x => x.Address is not null);
        RuleFor(x => x.Address.Country).NotEmpty().When(x => x.Address is not null);
    }
}

public sealed class CreateShipmentManuallyCommandHandler(
    IShippingDbContext dbContext,
    IShipmentCreationService creationService,
    IAuditLogWriter auditLogWriter) : IRequestHandler<CreateShipmentManuallyCommand, Result<CreateShipmentManuallyResponse>>
{
    public async Task<Result<CreateShipmentManuallyResponse>> Handle(CreateShipmentManuallyCommand request, CancellationToken cancellationToken)
    {
        var requestHash = ComputeRequestHash(request.OrderId, request.Address);

        var existingKey = await dbContext.IdempotencyKeys.FindAsync([request.IdempotencyKey], cancellationToken);
        if (existingKey is not null)
        {
            if (existingKey.RequestHash != requestHash)
            {
                return Result.Failure<CreateShipmentManuallyResponse>(
                    Error.Custom("idempotency_key_conflict", $"Idempotency-Key '{request.IdempotencyKey}' was already used with a different request body."));
            }

            // Same key, same body - replay the original outcome. A prior 409 replays as the same
            // Problem envelope (not a ShipmentView - api-contract.yaml's 409 response schema is
            // Problem); only a prior 202 replays a body, re-derived fresh from the shipment's
            // current state (see ShipmentIdempotencyKey's doc comment).
            if (existingKey.ResponseStatus == 409)
            {
                return Result.Failure<CreateShipmentManuallyResponse>(Error.Conflict($"A shipment already exists for order '{request.OrderId}'."));
            }

            var current = await dbContext.Shipments.AsNoTracking().FirstAsync(s => s.Id == ShipmentId.From(existingKey.ShipmentId), cancellationToken);
            return Result.Success(new CreateShipmentManuallyResponse(existingKey.ResponseStatus, ToView(current)));
        }

        var orderId = OrderId.From(Guid.Parse(request.OrderId));
        var address = Domain.ValueObjects.Address.Create(request.Address.Line1, request.Address.Line2, request.Address.City, request.Address.State, request.Address.PostalCode, request.Address.Country);

        var outcome = await creationService.CreateAsync(orderId, address, request.ActorId, cancellationToken);
        // Plain int literals, not Microsoft.AspNetCore.Http.StatusCodes - Application stays free
        // of any ASP.NET Core dependency (202 Accepted / 409 Conflict).
        var statusCode = outcome.AlreadyExisted ? 409 : 202;

        var shipment = await dbContext.Shipments.AsNoTracking().FirstAsync(s => s.Id == outcome.ShipmentId, cancellationToken);

        // Recorded regardless of outcome (202 or 409) so a retry with the same key+body always
        // replays the exact original result, never re-running the existence check a second time.
        var idempotencyKey = ShipmentIdempotencyKey.Create(request.IdempotencyKey, requestHash, shipment.Id.Value, statusCode);
        idempotencyKey.CreatedBy = request.ActorId;
        idempotencyKey.UpdatedBy = request.ActorId;
        dbContext.IdempotencyKeys.Add(idempotencyKey);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!outcome.AlreadyExisted)
        {
            await auditLogWriter.WriteAsync(
                AuditLogEntry.Create("kart-shipping-service", request.ActorId, "ops-principal", "ShipmentCreatedManually", "Shipment", shipment.Id.ToString(), new Dictionary<string, object?> { ["orderId"] = request.OrderId }),
                cancellationToken);
        }

        if (outcome.AlreadyExisted)
        {
            return Result.Failure<CreateShipmentManuallyResponse>(Error.Conflict($"A shipment already exists for order '{request.OrderId}'."));
        }

        return Result.Success(new CreateShipmentManuallyResponse(statusCode, ToView(shipment)));
    }

    private static string ComputeRequestHash(string orderId, AddressDto address)
    {
        var canonical = $"{orderId}|{address.Line1}|{address.Line2}|{address.City}|{address.State}|{address.PostalCode}|{address.Country}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static ShipmentView ToView(Shipment shipment) => new(
        shipment.Id.Value,
        shipment.OrderId.ToString(),
        shipment.Status.ToString(),
        shipment.Carrier?.Value,
        shipment.TrackingId?.Value,
        shipment.FailureReason?.Value,
        shipment.CreatedAt,
        shipment.Status == Domain.Enums.ShipmentStatus.Dispatched ? shipment.UpdatedAt : null,
        shipment.Status == Domain.Enums.ShipmentStatus.Failed ? shipment.UpdatedAt : null);
}
