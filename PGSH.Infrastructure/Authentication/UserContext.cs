using PGSH.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using PGSH.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;
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
            user.LinkIdentity(keycloakId.ToString());
            await _dbContext.SaveChangesAsync(cancellationToken);
            _memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(60));
            return;
        }

        throw new ApplicationException($"Domain profile for {email} not found.");

    }
}
