using Kart.Shipping.Application.Features.ListShipments;

namespace Kart.Shipping.UnitTests.Features.ListShipments;

public class ListShipmentsQueryValidatorTests
{
    private readonly ListShipmentsQueryValidator _validator = new();

    [Fact]
    public void Validate_LimitOutOfRange_Fails()
    {
        var result = _validator.Validate(new ListShipmentsQuery(null, null, null, null, 0));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_InvalidStatus_Fails()
    {
        var result = _validator.Validate(new ListShipmentsQuery(null, "NotAStatus", null, null, 50));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ValidQuery_Passes()
    {
        var result = _validator.Validate(new ListShipmentsQuery("order-1", "Dispatched", "Primary", null, 50));
        Assert.True(result.IsValid);
    }
}
