using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Users.GetCurrent;

public sealed record GetCurrentUserQuery() : IQuery<UserResponse>;
