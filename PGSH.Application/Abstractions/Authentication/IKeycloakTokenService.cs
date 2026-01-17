namespace PGSH.Application.Abstractions.Authentication;

public record TokenResponse(string AccessToken, int ExpiresIn, string RefreshToken, int RefreshExpiresIn, string Scope, string TokenType);
public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string RefreshToken);
public interface IKeycloakTokenService
{
    /// <summary>
    /// Exchanges user credentials for an Access Token and Refresh Token via Keycloak's Direct Grant flow.
    /// </summary>
    /// <param name="request">User's email and password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A TokenResponse containing tokens or null if authentication fails.</returns>
    Task<TokenResponse?> GetTokenAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges an existing Refresh Token for a new Access Token and Refresh Token.
    /// </summary>
    /// <param name="request">The Refresh Token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A TokenResponse containing new tokens or null if refresh fails.</returns>
    Task<TokenResponse?> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
}
