using RabbitMQ.Client.Events;

namespace Kart.Shipping.Infrastructure.Messaging;

/// <summary>Reads/writes this service's own retry-count header (never RabbitMQ's native `x-death`) - mirrors kart-payment-service's identically-shaped helper.</summary>
public static class RetryHeaders
{
    public static int GetRetryCount(RabbitMQ.Client.IBasicProperties properties, string headerName)
    {
        if (properties.Headers is not null && properties.Headers.TryGetValue(headerName, out var value) && value is not null)
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.Parse(System.Text.Encoding.UTF8.GetString(bytes)),
                _ => 0
            };
        }

        return 0;
    }
}
