using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Users;
using PGSH.SharedKernel;

namespace PGSH.Application.Users.GetCurrent;

internal sealed class GetCurrentUserQueryHandler(IApplicationDbContext dbContext, IUserContext userContext)
    : IQueryHandler<GetCurrentUserQuery, UserResponse>
{
    public async Task<Result<UserResponse>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var keycloakId = userContext.UserId;

        var user = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.IdentityProviderId == keycloakId.ToString())
            .Select(u => new UserResponse(u.Id, u.Email, u.FirstName, u.LastName))
            .SingleOrDefaultAsync(cancellationToken);

        return user is null
            ? Result.Failure<UserResponse>(UserErrors.NotFound(keycloakId))
            : user;
    }
}
