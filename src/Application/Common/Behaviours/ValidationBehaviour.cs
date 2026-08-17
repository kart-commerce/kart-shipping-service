using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ValidationException = FluentValidation.ValidationException;

namespace Kart.Shipping.Application.Common.Behaviours;

/// <summary>Runs every registered `IValidator&lt;TRequest&gt;` in parallel, aggregates all failures, and throws once if any exist - a no-op pass-through when no validator is registered for `TRequest`. Mirrors kart-identity-service's identically-shaped behaviour.</summary>
public sealed class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators, ILogger<ValidationBehaviour<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count != 0)
        {
            logger.LogWarning("Stage {Stage}: {RequestName} rejected - {Errors}", "ValidationFailed", typeof(TRequest).Name, string.Join("; ", failures.Select(f => f.ErrorMessage)));
            throw new ValidationException(failures);
        }

        return await next();
    }
}
