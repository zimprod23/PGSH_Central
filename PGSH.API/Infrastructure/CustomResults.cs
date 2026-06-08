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

        static int GetStatusCode(ErrorType errorType) => errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
