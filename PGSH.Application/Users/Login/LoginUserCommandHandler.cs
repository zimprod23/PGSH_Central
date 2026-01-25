using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Users;
using Microsoft.EntityFrameworkCore;
using PGSH.SharedKernel;

namespace PGSH.Application.Users.Login;

internal sealed class LoginUserCommandHandler(
    IApplicationDbContext context
    //IPasswordHasher passwordHasher,
    //ITokenProvider tokenProvider
    
    ) : ICommandHandler<LoginUserCommand, string>
{
    public async Task<Result<string>> Handle(LoginUserCommand command, CancellationToken cancellationToken)
    {
        User? user =
            await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Email == command.Email, cancellationToken);

        if (user is null)
        {
            return Result.Failure<string>(UserErrors.NotFoundByEmail);
        }

        bool verified = true;//passwordHasher.Verify(command.Password, user.PasswordHash);

        if (!verified)
        {
            return Result.Failure<string>(UserErrors.NotFoundByEmail);
        }

        string token = "";//tokenProvider.Create(user);

        return token;
    }
}
