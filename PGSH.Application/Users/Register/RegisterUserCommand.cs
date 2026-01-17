using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Users.Register;

public sealed record RegisterUserCommand(string Email, string FirstName, string LastName, string Password)
    : ICommand<Guid>;
