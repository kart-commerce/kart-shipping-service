using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Application.Common.Models;

namespace Kart.Shipping.IntegrationTests;

/// <summary>
/// The primary real-DB, real-broker end-to-end path: publish a genuine `OrderConfirmed` message
/// onto a real RabbitMQ exchange, and observe it flow through SHIP-1 (consume+persist) → SHIP-2
/// (carrier resolution) → SHIP-3 (publish resolved event) → the Mongo read-model projector →
/// SHIP-4/5's Mongo-backed GET endpoints, entirely against real Postgres/Mongo/RabbitMQ
/// containers - no mocks anywhere in this path.
/// </summary>
[Collection(nameof(ShippingTestCollection))]
public class OrderConfirmedFlowTests(ShippingApiFactory factory)
{
    [Fact]
    public async Task OrderConfirmed_DefaultAddress_EventuallyDispatchesViaPrimaryAndIsVisibleInReadModel()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueOpsToken("shipments-read"));
        var orderId = Guid.NewGuid().ToString();

        await TestOrderConfirmedPublisher.PublishAsync(factory, orderId);

        var page = await Polling.UntilAsync(
            () => client.GetFromJsonAsync<ShipmentPage>($"/v1/shipments?orderId={orderId}"),
            p => p.Items.Count == 1 && p.Items[0].Status == "Dispatched");

        var view = Assert.Single(page.Items);
        Assert.Equal("Primary", view.Carrier);
        Assert.NotNull(view.TrackingId);
        Assert.NotNull(view.DispatchedAt);

        var getResponse = await client.GetFromJsonAsync<ShipmentView>($"/v1/shipments/{view.ShipmentId}");
        Assert.Equal(view.ShipmentId, getResponse!.ShipmentId);
    }

    [Fact]
    public async Task OrderConfirmed_AddressRejectedByEveryCarrier_EventuallyFails()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueOpsToken("shipments-read"));
        var orderId = Guid.NewGuid().ToString();

        await TestOrderConfirmedPublisher.PublishAsync(factory, orderId, postalCode: "00000");

        var page = await Polling.UntilAsync(
            () => client.GetFromJsonAsync<ShipmentPage>($"/v1/shipments?orderId={orderId}"),
            p => p.Items.Count == 1 && p.Items[0].Status == "Failed");

        var view = Assert.Single(page.Items);
        Assert.Equal("No configured carrier could service this destination address.", view.FailureReason);
        Assert.NotNull(view.FailedAt);
    }

    [Fact]
    public async Task OrderConfirmed_RedeliveredForSameOrder_NeverCreatesASecondShipment()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueOpsToken("shipments-read"));
        var orderId = Guid.NewGuid().ToString();

        await TestOrderConfirmedPublisher.PublishAsync(factory, orderId);
        await Polling.UntilAsync(
            () => client.GetFromJsonAsync<ShipmentPage>($"/v1/shipments?orderId={orderId}"),
            p => p.Items.Count == 1 && p.Items[0].Status == "Dispatched");

        // Redelivery - a duplicate/at-least-once-retried copy of the same OrderConfirmed.
        await TestOrderConfirmedPublisher.PublishAsync(factory, orderId);
        await Task.Delay(2000);

        var finalPage = await client.GetFromJsonAsync<ShipmentPage>($"/v1/shipments?orderId={orderId}");
        Assert.Single(finalPage!.Items);
    }
}

[CollectionDefinition(nameof(ShippingTestCollection))]
public class ShippingTestCollection : ICollectionFixture<ShippingApiFactory>;
