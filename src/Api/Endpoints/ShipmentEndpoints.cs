using Kart.Shared.ErrorHandling;
using Kart.Shared.Observability;
using Kart.Shipping.Api.Common;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Application.Features.CreateShipmentManually;
using Kart.Shipping.Application.Features.GetShipment;
using Kart.Shipping.Application.Features.ListShipments;
using Kart.Shipping.Infrastructure.Security;
using MediatR;

namespace Kart.Shipping.Api.Endpoints;

/// <summary>SHIP-4/SHIP-5/SHIP-6 - api-contract.yaml's `/v1/shipments` surface. Internal/ops-only (never routed through the public API Gateway or kart-admin-service - ddd-model.md Modeling Decision #8).</summary>
public static class ShipmentEndpoints
{
    public static void MapShipmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1/shipments").WithTags("Shipments");

        group.MapGet("/", ListShipmentsAsync).RequireAuthorization(AuthenticationExtensions.ShipmentsReadPolicy);
        group.MapGet("/{id:guid}", GetShipmentAsync).RequireAuthorization(AuthenticationExtensions.ShipmentsReadPolicy);
        group.MapPost("/", CreateShipmentManuallyAsync).RequireAuthorization(AuthenticationExtensions.ShipmentsWritePolicy);
    }

    private static async Task<IResult> ListShipmentsAsync(
        ISender sender,
        HttpContext httpContext,
        string? orderId,
        string? status,
        string? carrier,
        string? cursor,
        int limit = 50)
    {
        using var flow = KartFlowContext.Push(FlowNames.ShipmentFulfillment);
        var page = await sender.Send(new ListShipmentsQuery(orderId, status, carrier, cursor, limit));
        return Results.Ok(page);
    }

    private static async Task<IResult> GetShipmentAsync(ISender sender, HttpContext httpContext, Guid id)
    {
        using var flow = KartFlowContext.Push(FlowNames.ShipmentFulfillment);
        var result = await sender.Send(new GetShipmentQuery(id));
        return result.ToHttpResult(httpContext, view => Results.Ok(view));
    }

    private static async Task<IResult> CreateShipmentManuallyAsync(ISender sender, HttpContext httpContext, ICurrentPrincipal currentPrincipal, CreateShipmentRequest request)
    {
        using var flow = KartFlowContext.Push(FlowNames.ShipmentFulfillment);

        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyHeader) || string.IsNullOrWhiteSpace(idempotencyKeyHeader))
        {
            var problem = KartProblemDetailsFactory.Create(httpContext, StatusCodes.Status400BadRequest, "validation_error", "The Idempotency-Key header is required.");
            return Results.Json(problem, statusCode: StatusCodes.Status400BadRequest, contentType: "application/problem+json");
        }

        var actorId = currentPrincipal.Subject ?? "unknown-ops-principal";
        var command = new CreateShipmentManuallyCommand(idempotencyKeyHeader.ToString(), request.OrderId, request.Address, actorId);

        var result = await sender.Send(command);
        return result.ToHttpResult(httpContext, response => Results.Json(response.View, statusCode: response.StatusCode));
    }
}
