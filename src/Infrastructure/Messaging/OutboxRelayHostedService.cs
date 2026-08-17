using System.Text;
using Kart.Shared.Messaging;
using Kart.Shared.Observability;
using Kart.Shipping.Application.Common;
using Kart.Shipping.Domain.Enums;
using Kart.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Kart.Shipping.Infrastructure.Messaging;

/// <summary>
/// SHIP-3 - relays `shipment_outbox` rows resolved by SHIP-2 (`ShipmentDispatched`/
/// `ShipmentCreationFailed` only - `CarrierCallRequested` is internal-only, never relayed here,
/// see contracts/README.md) to whichever exchange/routing key
/// contracts/message-bus-manifest.json's `publishedEvents` maps each event type to. Retries
/// indefinitely until RabbitMQ is reachable, rather than dead-lettering. Mirrors
/// kart-identity-service's/kart-payment-service's identically-shaped relay.
/// </summary>
public sealed class OutboxRelayHostedService(
    IServiceScopeFactory scopeFactory,
    IConnectionFactory connectionFactory,
    MessageBusManifest manifest,
    ILogger<OutboxRelayHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();
                RabbitMqTopologyProvisioner.Declare(channel, manifest);

                await RunRelayLoopAsync(channel, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Shipping outbox relay lost its RabbitMQ connection; reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task RunRelayLoopAsync(IModel channel, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RelayPendingBatchAsync(channel, stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task RelayPendingBatchAsync(IModel channel, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();

        var pending = await dbContext.ShipmentOutboxEvents
            .Where(e => e.ProcessedAt == null && (e.MessageType == ShipmentOutboxEventType.ShipmentDispatched || e.MessageType == ShipmentOutboxEventType.ShipmentCreationFailed))
            .OrderBy(e => e.OutboxSeq)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        using var flowScope = KartFlowContext.Push(FlowNames.ShipmentFulfillment);

        foreach (var outboxEvent in pending)
        {
            var eventType = outboxEvent.MessageType.ToString();
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = outboxEvent.Id.ToString();
            properties.ContentType = "application/json";

            var exchange = manifest.ExchangeFor(eventType);
            var routingKey = manifest.RoutingKeyFor(eventType);

            using var activity = RabbitMqTraceContext.StartPublishActivityFromStoredTraceParent(exchange, routingKey, outboxEvent.TraceParent, properties);

            channel.BasicPublish(exchange: exchange, routingKey: routingKey, basicProperties: properties, body: Encoding.UTF8.GetBytes(outboxEvent.Payload));

            outboxEvent.MarkProcessed(DateTimeOffset.UtcNow);
            outboxEvent.UpdatedBy = SystemPrincipals.OutboxRelayPoller;

            logger.LogInformation("Stage {Stage}: {EventType} outbox event {OutboxId} published to {Exchange}/{RoutingKey}", "OutboxEventPublished", eventType, outboxEvent.Id, exchange, routingKey);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
