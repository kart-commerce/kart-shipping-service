using Kart.Shipping.Application.Common;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Domain.ValueObjects;
using Kart.Shipping.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kart.Shipping.UnitTests.Application;

public class ShipmentCreationServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Address SomeAddress = Address.Create("1 Main St", null, "Metropolis", null, "55555", "US");

    [Fact]
    public async Task CreateAsync_NewOrder_PersistsPendingShipmentAndCarrierCallRequestedOutboxRow()
    {
        await using var dbContext = InMemoryDbContextFactory.Create(FixedNow);
        var service = new ShipmentCreationService(dbContext, new FixedDateTimeProvider(FixedNow), NullLogger<ShipmentCreationService>.Instance);
        var orderId = OrderId.From(Guid.NewGuid());

        var outcome = await service.CreateAsync(orderId, SomeAddress, "system:test", CancellationToken.None);

        Assert.False(outcome.AlreadyExisted);

        var shipment = Assert.Single(dbContext.Shipments);
        Assert.Equal(ShipmentStatus.Pending, shipment.Status);
        Assert.Equal(orderId, shipment.OrderId);
        Assert.Equal("system:test", shipment.CreatedBy);

        var outboxEvent = Assert.Single(dbContext.ShipmentOutboxEvents);
        Assert.Equal(ShipmentOutboxEventType.CarrierCallRequested, outboxEvent.MessageType);
        Assert.Equal(shipment.Id, outboxEvent.ShipmentId);
        Assert.Contains(orderId.ToString(), outboxEvent.Payload);
    }

    [Fact]
    public async Task CreateAsync_OrderAlreadyHasShipment_IsNoOpAndDoesNotInsertAnotherOutboxRow()
    {
        await using var dbContext = InMemoryDbContextFactory.Create(FixedNow);
        var service = new ShipmentCreationService(dbContext, new FixedDateTimeProvider(FixedNow), NullLogger<ShipmentCreationService>.Instance);
        var orderId = OrderId.From(Guid.NewGuid());

        var first = await service.CreateAsync(orderId, SomeAddress, "system:test", CancellationToken.None);
        var second = await service.CreateAsync(orderId, SomeAddress, "system:test", CancellationToken.None);

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.ShipmentId, second.ShipmentId);
        Assert.Single(dbContext.Shipments);
        Assert.Single(dbContext.ShipmentOutboxEvents);
    }
}
