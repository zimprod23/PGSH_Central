namespace PGSH.API.Extensions;

/// <summary>
/// The Keycloak client the API's documentation UIs authenticate through — Scalar and Swagger.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Named once here because it is one half of a pair, and the pair drifted.</b> Swagger was
/// configured for <c>pgsh-swagger</c>, a client that existed only in the Keycloak container's volume.
/// When that volume was lost on 17/09/2026 and the realm was rebuilt from
/// <c>keycloak/pgsh-realm.json</c> — which declares <b>one</b> client — the name in the code stopped
/// naming anything, and pressing « Authorize » landed on Keycloak's own
/// <i>« Client not found »</i> page. Nothing in the build could see it: the realm is a JSON file
/// nothing compiles, and the client id was a string literal.</para>
///
/// <para><b>The fix is one client, not a second one.</b> Adding <c>pgsh-swagger</c> back to the realm
/// would recreate exactly the two-lists-that-must-agree shape that broke, and the versioned realm is
/// deliberately minimal. <c>pgsh-frontend</c> is public, has standard flow and PKCE <c>S256</c>, and
/// its redirect URIs already cover <c>http://localhost:*/*</c> and <c>https://localhost:*/*</c> — so
/// the API's own port needs no new entry. These UIs are mapped in Development only.</para>
///
/// <para>⚠ <b>And the coupling is pinned</b>: <c>KeycloakRealmFileTests</c> asserts this constant names
/// a client the realm file actually declares, and that the client can carry the flow. A name that
/// stops resolving now fails the build rather than a login.</para>
/// </remarks>
internal static class ApiDocumentationAuth
{
    internal const string ClientId = "pgsh-frontend";

    /// <summary>The name the OpenAPI document gives the scheme; Scalar preselects it by this name.</summary>
    internal const string SchemeName = "keycloak";

    internal static readonly string[] Scopes = ["openid", "profile"];
}
