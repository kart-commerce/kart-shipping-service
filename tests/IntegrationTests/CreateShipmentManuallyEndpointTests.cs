using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kart.Shipping.Api.Common;
using Kart.Shipping.Application.Common.Models;

namespace Kart.Shipping.IntegrationTests;

/// <summary>SHIP-6, against real Postgres - covers every documented Idempotency-Key contract case (api-contract.yaml).</summary>
[Collection(nameof(ShippingTestCollection))]
public class CreateShipmentManuallyEndpointTests(ShippingApiFactory factory)
{
    private static readonly AddressDto SomeAddress = new("1 Main St", null, "Metropolis", null, "55555", "US");

    private HttpClient CreateAuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueOpsToken("shipments-write", "shipments-read"));
        return client;
    }

    [Fact]
    public async Task Post_NewOrder_Returns202Accepted()
    {
        var client = CreateAuthorizedClient();
        var orderId = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(new CreateShipmentRequest(orderId, SomeAddress)) };
        request.Headers.Add("Idempotency-Key", $"key-{orderId}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<ShipmentView>();
        Assert.Equal("Pending", view!.Status);
    }

    [Fact]
    public async Task Post_SameKeyAndBodyTwice_ReplaysIdenticalResponse()
    {
        var client = CreateAuthorizedClient();
        var orderId = Guid.NewGuid().ToString();
        var key = $"key-{orderId}";

        HttpRequestMessage BuildRequest() => new(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(new CreateShipmentRequest(orderId, SomeAddress)), Headers = { { "Idempotency-Key", key } } };

        var first = await client.SendAsync(BuildRequest());
        var second = await client.SendAsync(BuildRequest());

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var firstView = await first.Content.ReadFromJsonAsync<ShipmentView>();
        var secondView = await second.Content.ReadFromJsonAsync<ShipmentView>();
        Assert.Equal(firstView!.ShipmentId, secondView!.ShipmentId);
    }

    [Fact]
    public async Task Post_SameKeyDifferentBody_Returns422()
    {
        var client = CreateAuthorizedClient();
        var orderId = Guid.NewGuid().ToString();
        var key = $"key-{orderId}";

        var first = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(new CreateShipmentRequest(orderId, SomeAddress)), Headers = { { "Idempotency-Key", key } } };
        await client.SendAsync(first);

        var differentAddress = SomeAddress with { Line1 = "999 Other St" };
        var second = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(new CreateShipmentRequest(orderId, differentAddress)), Headers = { { "Idempotency-Key", key } } };
        var response = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SameOrderDifferentKey_Returns409()
    {
        var client = CreateAuthorizedClient();
        var orderId = Guid.NewGuid().ToString();

        var first = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(new CreateShipmentRequest(orderId, SomeAddress)), Headers = { { "Idempotency-Key", $"key-a-{orderId}" } } };
        await client.SendAsync(first);

        var second = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(new CreateShipmentRequest(orderId, SomeAddress)), Headers = { { "Idempotency-Key", $"key-b-{orderId}" } } };
        var response = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_MissingIdempotencyKeyHeader_Returns400()
    {
        var client = CreateAuthorizedClient();
        var response = await client.PostAsJsonAsync("/v1/shipments", new CreateShipmentRequest(Guid.NewGuid().ToString(), SomeAddress));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutAuthorization_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/shipments", new CreateShipmentRequest(Guid.NewGuid().ToString(), SomeAddress));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithReadOnlyScope_Returns403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueOpsToken("shipments-read"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments") { Content = JsonContent.Create(new CreateShipmentRequest(Guid.NewGuid().ToString(), SomeAddress)) };
        request.Headers.Add("Idempotency-Key", "some-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownShipmentId_Returns404()
    {
        var client = CreateAuthorizedClient();
        var response = await client.GetAsync($"/v1/shipments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
