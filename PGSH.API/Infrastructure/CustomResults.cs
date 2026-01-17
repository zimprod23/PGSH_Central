using PGSH.SharedKernel;

namespace PGSH.API.Infrastructure
{
    public static class CustomResults
    {
        public static IResult Problem(Result result)
        {
            if (result.IsSuccess)
            {
                throw new InvalidOperationException("Result is wrong");
            }
            return Results.Problem(
                 title: GetTitle(result.Error),
                 detail: GetDetail(result.Error),
                 type: GetType(result.Error.Type),
                 statusCode: GetStatusCode(result.Error.Type),
                 extensions: GetErrors(result));
            static string GetTitle(Error error) =>
                error.Type switch
                {
                    ErrorType.Problem => error.Code,
                    ErrorType.Conflict => error.Code,
                    ErrorType.Validation => error.Code,
                    ErrorType.NotFound => error.Code,
                    _ => "Internal server error"
                };
            static string GetDetail(Error error) =>
            error.Type switch
            {
                ErrorType.Validation => error.Description,
                ErrorType.Problem => error.Description,
                ErrorType.NotFound => error.Description,
                ErrorType.Conflict => error.Description,
                _ => "An unexpected error occurred"
            };

            static string GetType(ErrorType errorType) =>
                errorType switch
                {
                    ErrorType.Validation => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    ErrorType.Problem => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    ErrorType.NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    ErrorType.Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                    _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
                };

            static int GetStatusCode(ErrorType errorType) =>
                errorType switch
                {
                    ErrorType.Validation => StatusCodes.Status400BadRequest,
                    ErrorType.NotFound => StatusCodes.Status404NotFound,
                    ErrorType.Conflict => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status500InternalServerError
                };
            static Dictionary<string, object?> GetErrors(Result result)
            {
                if (result.Error is not ValidationError error)
                {
                    return null;
                }
                return new Dictionary<string, object?>
                {
                    { "errors", result.Error }
                };
            }
        }
    }
}
