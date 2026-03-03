using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.GetById;

public sealed record GetStageByIdQuery(int StageId): IQuery<StageResponse>;
