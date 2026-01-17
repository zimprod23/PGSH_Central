using System.Net.Http.Json;
using PGSH.Application.Abstractions.Authentication;

namespace PGSH.Infrastructure.Authentication;

/// <summary>
/// Service to communicate directly with the Keycloak token endpoint for login (Direct Grant) and token refresh.
/// </summary>
internal sealed class KeycloakTokenService : IKeycloakTokenService
{
    // The HttpClient is configured in Program.cs with the base Keycloak Authority URL (e.g., http://keycloak:8080/realms/fmpr)
    private readonly HttpClient _httpClient;
    private readonly string _clientId = "account"; // Matches the Audience injected by Aspire

    public KeycloakTokenService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<TokenResponse?> GetTokenAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _clientId,
            ["username"] = request.Email,
            ["password"] = request.Password,
            ["scope"] = "openid email profile"
        });

        return await ExecuteTokenRequestAsync(content, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TokenResponse?> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _clientId,
            ["refresh_token"] = request.RefreshToken
        });

        return await ExecuteTokenRequestAsync(content, cancellationToken);
    }

    private async Task<TokenResponse?> ExecuteTokenRequestAsync(FormUrlEncodedContent content, CancellationToken cancellationToken)
    {
        try
        {
            // Note: The path is always '/protocol/openid-connect/token' relative to the Authority/Realm URL
            var response = await _httpClient.PostAsync("protocol/openid-connect/token", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Log detailed error or throw a specific exception if needed
                return null;
            }

            // Keycloak returns a standard OIDC token response
            var tokenData = await response.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken);

            if (tokenData == null)
            {
                return null;
            }

            return new TokenResponse(
                tokenData.access_token,
                tokenData.expires_in,
                tokenData.refresh_token,
                tokenData.refresh_expires_in,
                tokenData.scope,
                tokenData.token_type
            );
        }
        catch (Exception ex)
        {
            // Log exception
            return null;
        }
    }

    // DTO to match Keycloak's standard JSON response fields
    private record TokenResponseDto(
        string access_token,
        int expires_in,
        string refresh_token,
        int refresh_expires_in,
        string scope,
        string token_type
    );
}