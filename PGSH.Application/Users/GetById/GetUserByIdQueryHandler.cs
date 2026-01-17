using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;
using PGSH.SharedKernel;

namespace PGSH.Application.Users.GetById;

internal sealed class GetUserByIdQueryHandler(IApplicationDbContext context, IUserContext userContext)
    : IQueryHandler<GetUserByIdQuery, UserResponse>
{
    public async Task<Result<UserResponse>> Handle(GetUserByIdQuery query, CancellationToken cancellationToken)
    {
        if (query.UserId != userContext.UserId)
        {
            return Result.Failure<UserResponse>(UserErrors.Unauthorized());
        }

        UserResponse? user = await context.Users
            .Where(u => u.Id == query.UserId)
            .Select(u => new UserResponse
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserResponse>(UserErrors.NotFound(query.UserId));
        }

        return user;
    }
}
