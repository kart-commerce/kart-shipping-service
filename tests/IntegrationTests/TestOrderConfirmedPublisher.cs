using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Kart.Shipping.IntegrationTests;

/// <summary>Stands in for kart-order-service - publishes a real `OrderConfirmed` message directly onto `order.exchange` (declared by this service itself as an external exchange at startup, per `RabbitMqTopologyProvisioner`). Retries the publish briefly since topology declaration at host startup is fire-and-forget.</summary>
public static class TestOrderConfirmedPublisher
{
    public static async Task PublishAsync(ShippingApiFactory factory, string orderId, string postalCode = "55555")
    {
        var payload = JsonSerializer.Serialize(new
        {
            orderId,
            address = new { line1 = "1 Main St", city = "Metropolis", postalCode, country = "US" }
        });

        var factoryConnection = new ConnectionFactory
        {
            HostName = factory.RabbitMqHostName,
            Port = factory.RabbitMqPort,
            UserName = "shipping",
            Password = "shipping"
        };

        var lastException = default(Exception);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var connection = factoryConnection.CreateConnection();
                using var channel = connection.CreateModel();
                channel.ExchangeDeclarePassive("order.exchange");

                var properties = channel.CreateBasicProperties();
                properties.ContentType = "application/json";
                channel.BasicPublish("order.exchange", "order.order.confirmed", properties, Encoding.UTF8.GetBytes(payload));
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException("Could not publish test OrderConfirmed message - order.exchange never appeared.", lastException);
    }
}
