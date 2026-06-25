using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Bulk;

// Stage-level equivalents of StartCohortAssignmentsCommand / CompletePeriodsCommand: act on the
// whole selection in ONE round-trip instead of one request per cohort. CohortIds scopes to an
// explicit selection (the Suivi UI); PartitionLabels scopes to whole rotations; PeriodNumbers
// narrows to a window of periods. All optional — null/empty widens the scope.
public sealed record StartStagePeriodsCommand(
    int StageId,
    IReadOnlyList<int>? CohortIds = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : ICommand<int>;

public sealed record CompleteStagePeriodsCommand(
    int StageId,
    IReadOnlyList<int>? CohortIds = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : ICommand<int>;

internal sealed class StartStagePeriodsCommandHandler(StagePeriodRunner runner)
    : ICommandHandler<StartStagePeriodsCommand, int>
{
    public Task<Result<int>> Handle(StartStagePeriodsCommand request, CancellationToken ct) =>
        runner.StartStageAsync(request.StageId, request.CohortIds, request.PartitionLabels, request.PeriodNumbers, ct);
}

internal sealed class CompleteStagePeriodsCommandHandler(StagePeriodRunner runner)
    : ICommandHandler<CompleteStagePeriodsCommand, int>
{
    public Task<Result<int>> Handle(CompleteStagePeriodsCommand request, CancellationToken ct) =>
        runner.CompleteStageAsync(request.StageId, request.CohortIds, request.PartitionLabels, request.PeriodNumbers, ct);
}
