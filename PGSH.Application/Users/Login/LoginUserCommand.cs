using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Users.Login;

public sealed record LoginUserCommand(string Email, string Password) : ICommand<string>;
