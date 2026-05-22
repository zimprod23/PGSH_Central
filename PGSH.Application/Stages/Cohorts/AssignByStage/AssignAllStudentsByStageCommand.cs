using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.AssignByStage;

public sealed record AssignAllStudentsByStageCommand(int StageId) : ICommand<BulkResponse<Guid, Guid>>;
