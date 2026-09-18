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

        var (statusCode, title, detail) = Describe(exception);

        // ⚠ Une panne de dépendance se journalise autrement qu'un défaut. Pendant une coupure, chaque
        // requête en cours lève la même exception : une ligne qui dit « la base est injoignable » se
        // lit d'un coup d'œil, là où vingt « Unhandled exception » identiques envoient chercher un
        // bug qui n'existe pas.
        logger.LogError(
            exception,
            statusCode == StatusCodes.Status503ServiceUnavailable
                ? "The database could not be reached"
                : "Unhandled exception occurred");

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Type   = ProblemType(statusCode),
            Title  = title,
            Detail = detail,
        };

        httpContext.Response.StatusCode = statusCode;

        // Use CancellationToken.None so that writing the error response itself
        // is not aborted if the original request token is already cancelled.
        await httpContext.Response.WriteAsJsonAsync(problemDetails, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Ce que l'on répond, et pourquoi les trois cas ne se confondent pas.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>503 n'est pas un 500 plus poli.</b> Il dit « le service est là, ce dont il dépend ne
    /// l'est pas » : il n'y a rien à corriger dans les données ni dans la demande, et la phrase qui
    /// l'accompagne est la seule chose qui dise à l'opérateur où aller. C'est pour cela qu'elle
    /// voyage dans <c>detail</c> — et que le client, qui masque le détail de tout ce qui est ≥ 500
    /// (un 500 peut porter n'importe quel interne), fait de ce code-là son exception.</para>
    ///
    /// <para><c>Failure</c> garde son « Server failure » sans détail : un défaut non identifié ne
    /// raconte rien de son intérieur à un navigateur.</para>
    /// </remarks>
    private static (int StatusCode, string Title, string? Detail) Describe(Exception exception) =>
        exception switch
        {
            DomainException domain => (domain.StatusCode, domain.Title, domain.Detail),

            _ when DatabaseOutage.IsReported(exception) => (
                StatusCodes.Status503ServiceUnavailable,
                "Base de données injoignable",
                "La base de données ne répond pas : la demande n'a rien enregistré. Réessayez dans "
                + "un moment — s'il faut agir, c'est sur le serveur de base de données, pas sur "
                + "l'application."),

            _ => (StatusCodes.Status500InternalServerError, "Server failure", null),
        };

    /// <summary>
    /// Le paragraphe de la RFC qui définit le code rendu. Il était figé sur celui du 500, donc tout
    /// document de problème renvoyait le lecteur vers la définition du mauvais code.
    /// </summary>
    private static string ProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest          => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1",
        StatusCodes.Status403Forbidden           => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3",
        StatusCodes.Status404NotFound            => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4",
        StatusCodes.Status409Conflict            => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8",
        StatusCodes.Status503ServiceUnavailable  => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.4",
        _                                        => "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
    };
}
