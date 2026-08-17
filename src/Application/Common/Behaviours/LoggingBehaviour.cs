using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Kart.Shipping.Application.Common.Behaviours;

/// <summary>Outermost pipeline stage (LoggingBehaviour wraps ValidationBehaviour - registration order is pipeline order). Never logs request/response payloads, only the type name and elapsed time - mirrors kart-identity-service's identically-shaped behaviour.</summary>
public sealed class LoggingBehaviour<TRequest, TResponse>(ILogger<LoggingBehaviour<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        logger.LogInformation("{RequestName} completed in {ElapsedMilliseconds}ms", typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
