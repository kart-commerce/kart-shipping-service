namespace Kart.Shipping.Infrastructure.Carriers;

/// <summary>Simulated transient carrier fault - what `Polly`'s retry/circuit-breaker pipeline reacts to. A real carrier SDK's own `HttpRequestException`/`TimeoutException` would play the identical role.</summary>
public sealed class SimulatedCarrierTimeoutException(string carrierCode) : Exception($"Simulated timeout calling carrier '{carrierCode}'.");
