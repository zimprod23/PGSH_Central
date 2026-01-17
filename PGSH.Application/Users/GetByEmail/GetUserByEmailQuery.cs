using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Users.GetByEmail;

public sealed record GetUserByEmailQuery(string Email) : IQuery<UserResponse>;
