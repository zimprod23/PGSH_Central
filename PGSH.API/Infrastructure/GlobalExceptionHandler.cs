using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PGSH.Infrastructure.Exceptions;

namespace PGSH.API.Infrastructure
{
    internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            logger.LogError(exception, "Unhandled exception occured");

            var (statusCode, title) = exception switch
            {
                // Handle your specific missing profile case
                UserProfileNotFoundException =>
                    (StatusCodes.Status403Forbidden, "Profile Not Found"),

                _ => (StatusCodes.Status500InternalServerError, "Server failure")
            };

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
                Title = title
            };
            httpContext.Response.StatusCode = problemDetails.Status.Value;
            await httpContext.Response.WriteAsJsonAsync(problemDetails);
            return true;
        }
    }
}
