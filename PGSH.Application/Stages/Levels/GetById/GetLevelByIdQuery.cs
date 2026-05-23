using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Levels.GetById;

public sealed record GetLevelByIdQuery(int Id) : IQuery<LevelResponse>;
