using System.Net;

namespace Kart.Shipping.IntegrationTests;

[Collection(nameof(ShippingTestCollection))]
public class HealthAndMetricsEndpointTests(ShippingApiFactory factory)
{
    [Fact]
    public async Task HealthLive_AlwaysReturns200()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_WithRealMigratedDatabase_Returns200()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_ExposesPrometheusScrapeFormat()
    {
        var response = await factory.CreateClient().GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aspnetcore_routing_match_attempts_total", body);
    }
}
