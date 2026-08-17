namespace Kart.Shipping.Application.Common.Models;

/// <summary>api-contract.yaml `ShipmentView` schema - identical shape whether served from a command handler's own response or the Mongo read model.</summary>
public sealed record ShipmentView(
    Guid ShipmentId,
    string OrderId,
    string Status,
    string? Carrier,
    string? TrackingId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? FailedAt);
