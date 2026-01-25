using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PGSH.Infrastructure.Authorization
{
    public class KeycloakRoleTransformer : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var identity = (ClaimsIdentity)principal.Identity!;

            // 1. Look for the 'realm_access' claim sent by Keycloak
            var realmAccessClaim = principal.FindFirst("realm_access");
            if (realmAccessClaim != null)
            {
                using var doc = JsonDocument.Parse(realmAccessClaim.Value);
                if (doc.RootElement.TryGetProperty("roles", out var roles))
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        var roleName = role.GetString();
                        if (!string.IsNullOrEmpty(roleName) && !principal.IsInRole(roleName))
                        {
                            // 2. Add it as a standard .NET Role
                            identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                        }
                    }
                }
            }
            return Task.FromResult(principal);
        }
    }
}
