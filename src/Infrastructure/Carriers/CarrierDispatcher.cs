using System.Collections.Concurrent;
using Kart.Shipping.Application.Common.Interfaces;
using Kart.Shipping.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Kart.Shipping.Infrastructure.Carriers;

/// <summary>
/// Walks `Carriers:Priority` in order, calling each <see cref="ICarrierClient"/> behind its OWN
/// circuit breaker + bounded retry pipeline - bulkhead-isolated per carrier, so the primary
/// carrier's own outage/open-circuit state can never throttle or block calls to the secondary
/// (design-decisions.md's explicit "a shared breaker would defeat the fallback's purpose" call).
/// </summary>
public interface ICarrierDispatcher
{
    Task<CarrierDispatchResult> DispatchAsync(Address address, CancellationToken cancellationToken);
}

public sealed record CarrierDispatchResult(bool Succeeded, string? Carrier, string? TrackingId, string? FailureReason);

public sealed class CarrierDispatcher(IEnumerable<ICarrierClient> clients, IOptions<CarrierOptions> options, ILogger<CarrierDispatcher> logger) : ICarrierDispatcher
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline> _pipelines = new();
    private readonly Dictionary<string, ICarrierClient> _clientsByCode = clients.ToDictionary(c => c.CarrierCode);

    public async Task<CarrierDispatchResult> DispatchAsync(Address address, CancellationToken cancellationToken)
    {
        var addressRejectedByEveryCarrier = true;

        foreach (var carrierCode in options.Value.Priority)
        {
            if (!_clientsByCode.TryGetValue(carrierCode, out var client))
            {
                logger.LogWarning("Carriers:Priority names unknown carrier code '{CarrierCode}' - skipping.", carrierCode);
                continue;
            }

            var pipeline = GetOrCreatePipeline(carrierCode);

            try
            {
                var result = await pipeline.ExecuteAsync(async ct => await client.RequestLabelAsync(address, ct), cancellationToken);

                if (result.AddressRejected)
                {
                    logger.LogInformation("Carrier {CarrierCode} rejected the destination address.", carrierCode);
                    continue;
                }

                addressRejectedByEveryCarrier = false;
                return new CarrierDispatchResult(Succeeded: true, carrierCode, result.TrackingId!.Value.Value, FailureReason: null);
            }
            catch (Exception ex) when (ex is SimulatedCarrierTimeoutException or BrokenCircuitException)
            {
                addressRejectedByEveryCarrier = false;
                logger.LogWarning(ex, "Carrier {CarrierCode} was unavailable (exhausted retries or open circuit); trying next configured carrier.", carrierCode);
            }
        }

        var reason = addressRejectedByEveryCarrier
            ? "No configured carrier could service this destination address."
            : "All configured carriers were unavailable after exhausting retries.";

        return new CarrierDispatchResult(Succeeded: false, Carrier: null, TrackingId: null, reason);
    }

    private ResiliencePipeline GetOrCreatePipeline(string carrierCode) =>
        // Strategy composition order matters: the FIRST-added strategy is OUTERMOST. Circuit
        // breaker must be outer/retry inner, so the breaker records exactly ONE sample per
        // logical DispatchAsync call - not one sample per individual retry attempt underneath
        // it. Getting this backwards (confirmed live: see contracts/README.md's testing notes)
        // let a single failing request's own internal retries alone satisfy MinimumThroughput and
        // trip the breaker, corrupting the next, unrelated request against the same carrier.
        _pipelines.GetOrAdd(carrierCode, code => new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SimulatedCarrierTimeoutException>(),
                FailureRatio = 0.5,
                MinimumThroughput = 3,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = args =>
                {
                    logger.LogWarning("Circuit breaker OPENED for carrier {CarrierCode} after repeated failures.", code);
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SimulatedCarrierTimeoutException>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Exponential
            })
            .Build());
}
