using PGSH.API.Infrastructure;
using PGSH.Application.Abstractions.Authentication;
using PGSH.SharedKernel;

namespace PGSH.API.Endpoints.Auth
{
    public sealed class Login : IEndpoint
    {
        public sealed record Request(string Email, string Password);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
           app.MapPost("users/login", async (
               Request request,
               IKeycloakTokenService tokenService,
               CancellationToken cancellationToken) =>
                {
                    var loginRequest = new LoginRequest(request.Email, request.Password);

                    // 1. Call the Keycloak token proxy service
                    TokenResponse? response = await tokenService.GetTokenAsync(loginRequest, cancellationToken);

                    if (response is null)
                    {
                        // 2. If Keycloak fails (invalid credentials), create a non-generic Problem failure result.
                        var errorResult = Result.Failure(
                            Error.Problem("Auth.Failed", "Invalid credentials provided. Check your email and password.")
                        );

                        // 3. Match against the custom problem result handler.
                        return CustomResults.Problem(errorResult);
                    }

                    // 3. Return Ok with the tokens
                    return Results.Ok(response);
            })
           .AllowAnonymous()
           .WithTags(Tags.Auth);
        }
    }
}
