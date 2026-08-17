using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace Kart.Shipping.ContractTests;

/// <summary>Validates contracts/api-contract.yaml both ways: (a) it still documents the shape SHIP-4/5/6 implement, and (b) the live endpoints actually match that documented shape. Mirrors kart-identity-service's ContractTests pattern.</summary>
public class ShipmentContractTests : IClassFixture<ShippingApiFactory>
{
    private readonly ShippingApiFactory _factory;

    public ShipmentContractTests(ShippingApiFactory factory) => _factory = factory;

    private static Dictionary<object, object> LoadContract()
    {
        var yaml = File.ReadAllText("api-contract.yaml");
        var deserializer = new DeserializerBuilder().Build();
        return deserializer.Deserialize<Dictionary<object, object>>(yaml);
    }

    [Fact]
    public void Contract_DefinesListShipmentsWithPageResponse()
    {
        var contract = LoadContract();
        var paths = (Dictionary<object, object>)contract["paths"];
        var shipmentsPath = (Dictionary<object, object>)paths["/v1/shipments"];
        var getOp = (Dictionary<object, object>)shipmentsPath["get"];

        Assert.Equal("listShipments", getOp["operationId"]);
        var responses = (Dictionary<object, object>)getOp["responses"];
        Assert.True(responses.ContainsKey("200"));
        Assert.True(responses.ContainsKey("401"));
    }

    [Fact]
    public void Contract_DefinesCreateShipmentManuallyWithIdempotencyKeyHeaderAndAllDocumentedStatusCodes()
    {
        var contract = LoadContract();
        var paths = (Dictionary<object, object>)contract["paths"];
        var shipmentsPath = (Dictionary<object, object>)paths["/v1/shipments"];
        var postOp = (Dictionary<object, object>)shipmentsPath["post"];

        Assert.Equal("createShipmentManually", postOp["operationId"]);

        var parameters = (List<object>)postOp["parameters"];
        var idempotencyKeyParam = parameters.Cast<Dictionary<object, object>>().Single(p => (string)p["name"] == "Idempotency-Key");
        Assert.True(Convert.ToBoolean(idempotencyKeyParam["required"]));
        Assert.Equal("header", idempotencyKeyParam["in"]);

        var responses = (Dictionary<object, object>)postOp["responses"];
        foreach (var expected in new[] { "202", "409", "422", "401" })
        {
            Assert.True(responses.ContainsKey(expected), $"api-contract.yaml no longer documents status {expected} for createShipmentManually");
        }
    }

    [Fact]
    public void Contract_DefinesGetShipmentByIdWith200And404()
    {
        var contract = LoadContract();
        var paths = (Dictionary<object, object>)contract["paths"];
        var byIdPath = (Dictionary<object, object>)paths["/v1/shipments/{id}"];
        var getOp = (Dictionary<object, object>)byIdPath["get"];

        Assert.Equal("getShipment", getOp["operationId"]);
        var responses = (Dictionary<object, object>)getOp["responses"];
        Assert.True(responses.ContainsKey("200"));
        Assert.True(responses.ContainsKey("404"));
    }

    [Fact]
    public async Task LiveEndpoint_ListShipments_MatchesDocumentedPageShape()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.IssueOpsToken("shipments-read"));

        var response = await client.GetAsync("/v1/shipments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("items", out _));
        Assert.True(body.RootElement.TryGetProperty("nextCursor", out _));
    }

    [Fact]
    public async Task LiveEndpoint_CreateShipmentManually_MatchesDocumentedShipmentViewShape()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.IssueOpsToken("shipments-write"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments")
        {
            Content = JsonContent.Create(new { orderId = Guid.NewGuid().ToString(), address = new { line1 = "1 Main St", city = "Metropolis", postalCode = "55555", country = "US" } })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var required in new[] { "shipmentId", "orderId", "status", "createdAt" })
        {
            Assert.True(body.RootElement.TryGetProperty(required, out _), $"ShipmentView response is missing required field '{required}'");
        }
        Assert.Equal("Pending", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task LiveEndpoint_GetUnknownShipment_Returns404WithProblemShape()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.IssueOpsToken("shipments-read"));

        var response = await client.GetAsync($"/v1/shipments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("errorCode", out _));
    }

    [Fact]
    public async Task LiveEndpoint_MissingCredential_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/shipments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
