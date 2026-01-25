//using PGSH.API.Infrastructure;
//using PGSH.Application.Abstractions.Authentication;
//using PGSH.SharedKernel;


//namespace PGSH.API.Endpoints.Auth;

//public sealed class RefreshToken : IEndpoint
//{
//    public sealed record Request(string RefreshToken);
//    public void MapEndpoint(IEndpointRouteBuilder app)
//    {
//        app.MapPost("users/refresh", async (
//            Request request,
//            IKeycloakTokenService tokenService,
//            CancellationToken cancellationToken) =>
//        {
//            var refreshRequest = new RefreshTokenRequest(request.RefreshToken);

//            // 1. Call the Keycloak refresh token proxy service
//            TokenResponse? response = await tokenService.RefreshTokenAsync(refreshRequest, cancellationToken);

//            if (response is null)
//            {
//                // 2. If Keycloak rejects the refresh token, create a non-generic Problem failure result.
//                var errorResult = Result.Failure(
//                    Error.Problem("Auth.RefreshFailed", "Invalid or expired refresh token. Please log in again.")
//                );

//                // 3. Match against the custom problem result handler.
//                return CustomResults.Problem(errorResult);
//            }

//            // 3. Return Ok with the new tokens
//            return Results.Ok(response);
//        })
//        .AllowAnonymous()
//        .WithTags(Tags.Auth);
//    }
//}
