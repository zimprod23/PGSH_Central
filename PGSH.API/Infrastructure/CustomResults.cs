using PGSH.SharedKernel;

namespace PGSH.API.Infrastructure;

public static class CustomResults
{
    public static IResult Problem(Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("Result is wrong");

        return Results.Problem(
            title: result.Error.Type == ErrorType.Failure ? "Internal server error" : result.Error.Code,
            detail: result.Error.Type == ErrorType.Failure ? "An unexpected error occurred" : result.Error.Description,
            type: GetType(result.Error.Type),
            statusCode: GetStatusCode(result.Error.Type),
            extensions: result.Error is ValidationError validationError
                ? new Dictionary<string, object?> { { "errors", validationError.Errors } }
                : null);

        static string GetType(ErrorType errorType) => errorType switch
        {
            ErrorType.NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            ErrorType.Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            ErrorType.Forbidden => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.5.1"
        };

        // ⚠ `ErrorType.Problem` means **a fault**, and it is named here so that it says so rather
        // than arriving at `_` by accident. It is a 500 deliberately: an unreachable backup archive
        // or a `pg_dump` out of disk is a panne, and `BackupEndpointTests` asserts exactly that.
        //
        // ⚠ **Nothing else may use it.** Thirteen sites did — « aucune année courante », « aucun
        // étudiant à rattacher », « fichier illisible » — and a 500 costs them their sentence:
        // `errorMiddleware` discards `detail` above 500 and shows « Une erreur serveur est
        // survenue ». The worst was `AcademicYearResolver`'s, the fallback of *every* handler that
        // omits a year. They are `Conflict` and `Validation` now, at their call sites, which is
        // where the two meanings had to be separated — never by widening this arm.
        static int GetStatusCode(ErrorType errorType) => errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Problem => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
