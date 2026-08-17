using System.Text;
using System.Text.Json;
using Kart.Shared.Messaging;
using Kart.Shared.Observability;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Application.Common.Models;
using Kart.Shipping.Application.Features.CreateShipmentOnOrderConfirmed;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kart.Shipping.Infrastructure.Messaging;

/// <summary>
/// SHIP-1's trigger - consumes `OrderConfirmed` from `shipping.order-events.queue` (bound to
/// kart-order-service's own `order.exchange`/`order.order.confirmed`, per
/// contracts/message-bus-manifest.json) and dispatches `CreateShipmentOnOrderConfirmedCommand`
/// through MediatR, so the same Logging/Validation pipeline behaviours apply. Mirrors
/// kart-identity-service's `UserDataErasedConsumerHostedService` shape exactly.
/// </summary>
public sealed class OrderConfirmedConsumerHostedService(
    IServiceScopeFactory scopeFactory,
    IConnectionFactory connectionFactory,
    MessageBusManifest manifest,
    ILogger<OrderConfirmedConsumerHostedService> logger) : BackgroundService
{
    private const string QueueName = "shipping.order-events.queue";
    private const string RetryCountHeader = "x-shipping-order-events-retry-count";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly QueueDefinition _queue = manifest.GetQueue(QueueName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();
                RabbitMqTopologyProvisioner.Declare(channel, manifest);
                channel.BasicQos(0, prefetchCount: 10, global: false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, deliverEventArgs) => await OnMessageReceivedAsync(channel, deliverEventArgs, stoppingToken);
                channel.BasicConsume(QueueName, autoAck: false, consumer);

                while (!stoppingToken.IsCancellationRequested && connection.IsOpen)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OrderConfirmed consumer lost its RabbitMQ connection; reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task OnMessageReceivedAsync(IModel channel, BasicDeliverEventArgs deliverEventArgs, CancellationToken stoppingToken)
    {
        using var activity = RabbitMqTraceContext.StartConsumeActivity(QueueName, deliverEventArgs.BasicProperties);
        using var flowScope = KartFlowContext.Push(FlowNames.ShipmentFulfillment);

        try
        {
            var json = Encoding.UTF8.GetString(deliverEventArgs.Body.Span);
            var payload = JsonSerializer.Deserialize<OrderConfirmedPayload>(json, SerializerOptions)
                ?? throw new InvalidOperationException("OrderConfirmed payload deserialized to null.");

            logger.LogInformation("Stage {Stage}: OrderConfirmed consumed for order {OrderId}", "OrderConfirmedRequestReceived", payload.OrderId);

            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new CreateShipmentOnOrderConfirmedCommand(payload.OrderId, payload.Address), stoppingToken);

            channel.BasicAck(deliverEventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            HandleFailure(channel, deliverEventArgs, ex);
        }
    }

    private void HandleFailure(IModel channel, BasicDeliverEventArgs deliverEventArgs, Exception ex)
    {
        var retryCount = RetryHeaders.GetRetryCount(deliverEventArgs.BasicProperties, RetryCountHeader);
        var tiers = _queue.RetryLadder?.Tiers ?? Array.Empty<RetryTierDefinition>();

        if (retryCount < tiers.Count)
        {
            var tier = tiers[retryCount];
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Headers = new Dictionary<string, object> { [RetryCountHeader] = retryCount + 1 };

            channel.BasicPublish(exchange: string.Empty, routingKey: tier.Name, basicProperties: properties, body: deliverEventArgs.Body);
            channel.BasicAck(deliverEventArgs.DeliveryTag, multiple: false);

            logger.LogWarning(ex, "OrderConfirmed processing failed; routed to retry tier {Tier} (attempt {Attempt}).", tier.Name, retryCount + 1);
        }
        else
        {
            logger.LogCritical(ex, "OrderConfirmed processing failed after exhausting all retry tiers; dead-lettering to {Dlq}.", _queue.DeadLetter?.RoutingKey);
            channel.BasicNack(deliverEventArgs.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private sealed record OrderConfirmedPayload(string OrderId, AddressDto Address);
}
