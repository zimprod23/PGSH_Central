using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Users.GetById;

public sealed record GetUserByIdQuery(Guid UserId) : IQuery<UserResponse>;
