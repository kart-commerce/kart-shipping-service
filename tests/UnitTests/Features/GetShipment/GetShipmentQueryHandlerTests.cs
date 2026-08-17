using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Application.Features.GetShipment;
using NSubstitute;

namespace Kart.Shipping.UnitTests.Features.GetShipment;

public class GetShipmentQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShipmentExists_ReturnsSuccessWithView()
    {
        var readRepository = Substitute.For<IShipmentReadRepository>();
        var shipmentId = Guid.NewGuid();
        var view = new ShipmentView(shipmentId, Guid.NewGuid().ToString(), "Dispatched", "Primary", "TRACK-1", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        readRepository.GetByIdAsync(shipmentId, Arg.Any<CancellationToken>()).Returns(view);

        var handler = new GetShipmentQueryHandler(readRepository);
        var result = await handler.Handle(new GetShipmentQuery(shipmentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(view, result.Value);
    }

    [Fact]
    public async Task Handle_ShipmentDoesNotExist_ReturnsNotFound()
    {
        var readRepository = Substitute.For<IShipmentReadRepository>();
        readRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ShipmentView?)null);

        var handler = new GetShipmentQueryHandler(readRepository);
        var result = await handler.Handle(new GetShipmentQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
