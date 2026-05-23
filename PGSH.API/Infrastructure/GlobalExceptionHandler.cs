using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PGSH.Infrastructure.Exceptions;

namespace PGSH.API.Infrastructure;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Client disconnected — not a real error, nothing to respond to.
        if (exception is OperationCanceledException)
            return true;

        logger.LogError(exception, "Unhandled exception occurred");

        var (statusCode, title) = exception switch
        {
            DomainException domain => (domain.StatusCode, domain.Title),
            _ => (StatusCodes.Status500InternalServerError, "Server failure")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
            Title = title
        };

        httpContext.Response.StatusCode = statusCode;

        // Use CancellationToken.None so that writing the error response itself
        // is not aborted if the original request token is already cancelled.
        await httpContext.Response.WriteAsJsonAsync(problemDetails, CancellationToken.None);
        return true;
    }
}
