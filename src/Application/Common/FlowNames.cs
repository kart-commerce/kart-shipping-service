namespace Kart.Shipping.Application.Common;

/// <summary>kart-conventions.md's per-business-flow tracing/logging tag, opened once via `KartFlowContext.Push` at each entry point.</summary>
public static class FlowNames
{
    public const string ShipmentFulfillment = "OrderFulfillmentShipmentCreation";
}
