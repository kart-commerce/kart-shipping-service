using Kart.Shipping.Domain.ValueObjects;
using Kart.Shipping.Infrastructure.Carriers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kart.Shipping.UnitTests.Infrastructure.Carriers;

/// <summary>
/// Verifies every documented postal-code sentinel scenario (contracts/README.md deviation #2)
/// against a FRESH dispatcher per test - a live end-to-end run against the real API caught a real
/// bug where a shared/long-lived circuit breaker state let one test's failures corrupt an
/// unrelated later request; each test here gets its own dispatcher precisely to also serve as a
/// regression test for that fix (composition order in <see cref="CarrierDispatcher"/>).
/// </summary>
public class CarrierDispatcherTests
{
    private static readonly Address DefaultAddress = Address.Create("1 Main St", null, "Metropolis", null, "22222", "US");

    private static CarrierDispatcher CreateDispatcher() => new(
        [new SimulatedPrimaryCarrierClient(), new SimulatedSecondaryCarrierClient()],
        Options.Create(new CarrierOptions()),
        NullLogger<CarrierDispatcher>.Instance);

    [Fact]
    public async Task DispatchAsync_DefaultAddress_SucceedsViaPrimary()
    {
        var result = await CreateDispatcher().DispatchAsync(DefaultAddress, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(SimulatedPrimaryCarrierClient.Code, result.Carrier);
        Assert.NotNull(result.TrackingId);
    }

    [Fact]
    public async Task DispatchAsync_PrimaryTimeoutSentinel_FallsBackToSecondary()
    {
        var address = DefaultAddress with { PostalCode = "11111" };

        var result = await CreateDispatcher().DispatchAsync(address, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(SimulatedSecondaryCarrierClient.Code, result.Carrier);
    }

    [Fact]
    public async Task DispatchAsync_BothRejectSentinel_FailsWithAddressRejectedReason()
    {
        var address = DefaultAddress with { PostalCode = "00000" };

        var result = await CreateDispatcher().DispatchAsync(address, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Carrier);
        Assert.Equal("No configured carrier could service this destination address.", result.FailureReason);
    }

    [Fact]
    public async Task DispatchAsync_BothTimeoutSentinel_FailsWithUnavailableReason()
    {
        var address = DefaultAddress with { PostalCode = "99999" };

        var result = await CreateDispatcher().DispatchAsync(address, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("All configured carriers were unavailable after exhausting retries.", result.FailureReason);
    }

    [Fact]
    public async Task DispatchAsync_UnrelatedSuccessAfterAnEarlierFailureOnTheSameDispatcher_StillSucceedsViaPrimary()
    {
        // Regression test for the live-discovered bug: a single failing dispatch's own internal
        // retries must never, by themselves, trip the circuit breaker for a later, unrelated
        // dispatch against the same carrier.
        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchAsync(DefaultAddress with { PostalCode = "11111" }, CancellationToken.None);

        var result = await dispatcher.DispatchAsync(DefaultAddress with { PostalCode = "33333" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(SimulatedPrimaryCarrierClient.Code, result.Carrier);
    }
}
