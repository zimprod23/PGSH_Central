using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.AssignByStage;

public sealed record AssignAllStudentsByStageCommand(
    int StageId,
    IReadOnlyList<string>? PartitionLabels = null,
    int? AcademicYearId = null) : ICommand<BulkResponse<Guid, Guid>>;
