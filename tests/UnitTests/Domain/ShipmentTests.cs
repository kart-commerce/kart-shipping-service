using Kart.Shipping.Domain.Entities;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Domain.Exceptions;
using Kart.Shipping.Domain.ValueObjects;

namespace Kart.Shipping.UnitTests.Domain;

public class ShipmentTests
{
    [Fact]
    public void CreateIntent_ReturnsPendingShipmentWithNoCarrierOrTracking()
    {
        var orderId = OrderId.From(Guid.NewGuid());

        var shipment = Shipment.CreateIntent(orderId);

        Assert.Equal(ShipmentStatus.Pending, shipment.Status);
        Assert.Equal(orderId, shipment.OrderId);
        Assert.Null(shipment.Carrier);
        Assert.Null(shipment.TrackingId);
        Assert.Null(shipment.FailureReason);
    }

    [Fact]
    public void MarkDispatched_FromPending_SetsCarrierAndTrackingAndStatus()
    {
        var shipment = Shipment.CreateIntent(OrderId.From(Guid.NewGuid()));

        shipment.MarkDispatched(Carrier.From("Primary"), TrackingId.From("TRACK-1"));

        Assert.Equal(ShipmentStatus.Dispatched, shipment.Status);
        Assert.Equal("Primary", shipment.Carrier!.Value.Value);
        Assert.Equal("TRACK-1", shipment.TrackingId!.Value.Value);
        Assert.Null(shipment.FailureReason);
    }

    [Fact]
    public void MarkFailed_FromPending_SetsFailureReasonAndStatus()
    {
        var shipment = Shipment.CreateIntent(OrderId.From(Guid.NewGuid()));

        shipment.MarkFailed(FailureReason.From("no carrier services this destination"));

        Assert.Equal(ShipmentStatus.Failed, shipment.Status);
        Assert.Equal("no carrier services this destination", shipment.FailureReason!.Value.Value);
        Assert.Null(shipment.Carrier);
        Assert.Null(shipment.TrackingId);
    }

    [Fact]
    public void MarkDispatched_WhenAlreadyDispatched_ThrowsInvalidShipmentTransition()
    {
        var shipment = Shipment.CreateIntent(OrderId.From(Guid.NewGuid()));
        shipment.MarkDispatched(Carrier.From("Primary"), TrackingId.From("TRACK-1"));

        Assert.Throws<InvalidShipmentTransitionException>(() =>
            shipment.MarkDispatched(Carrier.From("Secondary"), TrackingId.From("TRACK-2")));
    }

    [Fact]
    public void MarkFailed_WhenAlreadyFailed_ThrowsInvalidShipmentTransition()
    {
        var shipment = Shipment.CreateIntent(OrderId.From(Guid.NewGuid()));
        shipment.MarkFailed(FailureReason.From("first reason"));

        Assert.Throws<InvalidShipmentTransitionException>(() =>
            shipment.MarkFailed(FailureReason.From("second reason")));
    }

    [Fact]
    public void MarkFailed_WhenAlreadyDispatched_ThrowsInvalidShipmentTransition_MonotonicTerminalInvariant()
    {
        var shipment = Shipment.CreateIntent(OrderId.From(Guid.NewGuid()));
        shipment.MarkDispatched(Carrier.From("Primary"), TrackingId.From("TRACK-1"));

        Assert.Throws<InvalidShipmentTransitionException>(() => shipment.MarkFailed(FailureReason.From("late failure")));
    }
}
