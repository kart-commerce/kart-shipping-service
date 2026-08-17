using Kart.Shared.Domain;
using Kart.Shared.ErrorHandling;

namespace Kart.Shipping.Api.Common;

/// <summary>
/// Translates a handler's `Result`/`Result&lt;T&gt;` failure (design-decisions.md: "Domain/business
/// errors continue to use the Result/Either pattern rather than exceptions") into the same
/// `ProblemDetails` envelope `Kart.Shared.ErrorHandling`'s exception-mapping path produces for
/// thrown exceptions - one consistent error shape regardless of which path in a handler produced
/// the rejection.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, HttpContext httpContext, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : MapFailure(result.Error, httpContext);

    public static IResult MapFailure(Error error, HttpContext httpContext)
    {
        var statusCode = error.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            "idempotency_key_conflict" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = KartProblemDetailsFactory.Create(httpContext, statusCode, error.Code, error.Message);
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }
}
