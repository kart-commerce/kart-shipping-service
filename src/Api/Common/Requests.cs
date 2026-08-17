using Kart.Shipping.Application.Common.Models;

namespace Kart.Shipping.Api.Common;

/// <summary>api-contract.yaml `POST /v1/shipments` request body.</summary>
public sealed record CreateShipmentRequest(string OrderId, AddressDto Address);
