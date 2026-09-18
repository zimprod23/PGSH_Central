using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Infrastructure.Exceptions;
using System.Security.Claims;

namespace PGSH.Infrastructure.Authentication;

internal sealed class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IApplicationDbContext _dbContext;
    private readonly IMemoryCache _memoryCache;

    public UserContext(IHttpContextAccessor httpContextAccessor, IApplicationDbContext dbContext, IMemoryCache memoryCache)
    {
        _httpContextAccessor = httpContextAccessor;
        _memoryCache = memoryCache;
        _dbContext = dbContext;
    }

    public Guid UserId =>
        _httpContextAccessor
            .HttpContext?
            .User
            .GetUserId() ??
        throw new ApplicationException("User context is unavailable");

    public bool IsInRole(string role) =>
        _httpContextAccessor.HttpContext?.User.IsInRole(role) ?? false;

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;

        if (principal?.Identity is not { IsAuthenticated: true }) return;

        var keycloakId = UserId;
        var cacheKey = $"user_synced_{keycloakId}";

        if(_memoryCache.TryGetValue(cacheKey, out _)) return;

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.IdentityProviderId == keycloakId.ToString(), cancellationToken);

        if(user is not null)
        {
            _memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(60));
            return;
        }

        //Link by email
        var email = principal.FindFirstValue(ClaimTypes.Email);
        user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is not null)
        {
            // ⚠ Linking and RE-linking are different acts, and this is the path where the difference
            // shows. A record with no subject is a first link. A record carrying a *stale* one — the
            // ordinary state after an identity provider is rebuilt, since the new realm issues new
            // subjects while the restored database still holds the old ones — is a re-link, which
            // keeps what it replaced. Before the distinction existed, LinkIdentity refused here and a
            // realm rebuild locked out every user who had ever logged in.
            if (user.IdentityProviderId is null)
                user.LinkIdentity(keycloakId.ToString());
            else
                user.RelinkIdentity(keycloakId.ToString());

            await _dbContext.SaveChangesAsync(cancellationToken);
            _memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(60));
            return;
        }

        throw new UserProfileNotFoundException(keycloakId, email);

    }
}
