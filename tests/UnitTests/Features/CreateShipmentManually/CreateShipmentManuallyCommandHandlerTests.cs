using Kart.Shared.Auditing;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Application.Features.CreateShipmentManually;
using Kart.Shipping.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Kart.Shipping.UnitTests.Features.CreateShipmentManually;

public class CreateShipmentManuallyCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly AddressDto SomeAddress = new("1 Main St", null, "Metropolis", null, "55555", "US");

    [Fact]
    public async Task Handle_NewOrder_Returns202AndPersistsIdempotencyKey()
    {
        await using var dbContext = InMemoryDbContextFactory.Create(FixedNow);
        var creationService = new ShipmentCreationService(dbContext, new FixedDateTimeProvider(FixedNow), NullLogger<ShipmentCreationService>.Instance);
        var auditLogWriter = Substitute.For<IAuditLogWriter>();
        var handler = new CreateShipmentManuallyCommandHandler(dbContext, creationService, auditLogWriter);
        var orderId = Guid.NewGuid().ToString();

        var result = await handler.Handle(new Kart.Shipping.Application.Features.CreateShipmentManually.CreateShipmentManuallyCommand("key-1", orderId, SomeAddress, "ops-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.Value.StatusCode);
        Assert.Equal("Pending", result.Value.View.Status);
        Assert.Single(dbContext.IdempotencyKeys);
        await auditLogWriter.Received(1).WriteAsync(Arg.Any<AuditLogEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SameKeySameBody_ReplaysOriginalResponseWithoutCreatingAnotherShipment()
    {
        await using var dbContext = InMemoryDbContextFactory.Create(FixedNow);
        var creationService = new ShipmentCreationService(dbContext, new FixedDateTimeProvider(FixedNow), NullLogger<ShipmentCreationService>.Instance);
        var handler = new CreateShipmentManuallyCommandHandler(dbContext, creationService, Substitute.For<IAuditLogWriter>());
        var orderId = Guid.NewGuid().ToString();
        var command = new Kart.Shipping.Application.Features.CreateShipmentManually.CreateShipmentManuallyCommand("key-1", orderId, SomeAddress, "ops-1");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.View.ShipmentId, second.Value.View.ShipmentId);
        Assert.Equal(202, second.Value.StatusCode);
        Assert.Single(dbContext.Shipments);
        Assert.Single(dbContext.IdempotencyKeys);
    }

    [Fact]
    public async Task Handle_SameKeyDifferentBody_ReturnsIdempotencyKeyConflictError()
    {
        await using var dbContext = InMemoryDbContextFactory.Create(FixedNow);
        var creationService = new ShipmentCreationService(dbContext, new FixedDateTimeProvider(FixedNow), NullLogger<ShipmentCreationService>.Instance);
        var handler = new CreateShipmentManuallyCommandHandler(dbContext, creationService, Substitute.For<IAuditLogWriter>());
        var orderId = Guid.NewGuid().ToString();

        await handler.Handle(new Kart.Shipping.Application.Features.CreateShipmentManually.CreateShipmentManuallyCommand("key-1", orderId, SomeAddress, "ops-1"), CancellationToken.None);
        var differentAddress = SomeAddress with { Line1 = "999 Other St" };
        var conflict = await handler.Handle(new Kart.Shipping.Application.Features.CreateShipmentManually.CreateShipmentManuallyCommand("key-1", orderId, differentAddress, "ops-1"), CancellationToken.None);

        Assert.True(conflict.IsFailure);
        Assert.Equal("idempotency_key_conflict", conflict.Error.Code);
    }

    [Fact]
    public async Task Handle_SameOrderDifferentKey_Returns409Conflict()
    {
        await using var dbContext = InMemoryDbContextFactory.Create(FixedNow);
        var creationService = new ShipmentCreationService(dbContext, new FixedDateTimeProvider(FixedNow), NullLogger<ShipmentCreationService>.Instance);
        var handler = new CreateShipmentManuallyCommandHandler(dbContext, creationService, Substitute.For<IAuditLogWriter>());
        var orderId = Guid.NewGuid().ToString();

        await handler.Handle(new Kart.Shipping.Application.Features.CreateShipmentManually.CreateShipmentManuallyCommand("key-1", orderId, SomeAddress, "ops-1"), CancellationToken.None);
        var conflict = await handler.Handle(new Kart.Shipping.Application.Features.CreateShipmentManually.CreateShipmentManuallyCommand("key-2", orderId, SomeAddress, "ops-1"), CancellationToken.None);

        Assert.True(conflict.IsFailure);
        Assert.Equal("conflict", conflict.Error.Code);
        Assert.Single(dbContext.Shipments);
    }
}
