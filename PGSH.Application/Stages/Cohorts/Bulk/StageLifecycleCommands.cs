using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Bulk;

// Stage-level equivalents of StartCohortAssignmentsCommand / CompletePeriodsCommand: act on the
// whole selection in ONE round-trip instead of one request per cohort. CohortIds scopes to an
// explicit selection (the Suivi UI); PartitionLabels scopes to whole rotations; PeriodNumbers
// narrows to a window of periods. Those three are optional — null/empty widens the scope.
//
// AcademicYearId is the exception: omitted, it resolves to the current year, never to "all years".
// A stage keeps a cohort per (group, year), so widening it would have started or closed the
// rotations of every promotion that ever took the stage.
public sealed record StartStagePeriodsCommand(
    int StageId,
    int? AcademicYearId = null,
    IReadOnlyList<int>? CohortIds = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : ICommand<int>;

public sealed record CompleteStagePeriodsCommand(
    int StageId,
    int? AcademicYearId = null,
    IReadOnlyList<int>? CohortIds = null,
    IReadOnlyList<string>? PartitionLabels = null,
    IReadOnlyList<int>? PeriodNumbers = null) : ICommand<int>;

internal sealed class StartStagePeriodsCommandHandler(
    AcademicYearResolver yearResolver,
    StagePeriodRunner runner)
    : ICommandHandler<StartStagePeriodsCommand, int>
{
    public async Task<Result<int>> Handle(StartStagePeriodsCommand request, CancellationToken ct)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, ct);
        return year.IsFailure
            ? Result.Failure<int>(year.Error)
            : await runner.StartStageAsync(
                request.StageId, year.Value, request.CohortIds, request.PartitionLabels,
                request.PeriodNumbers, ct);
    }
}

internal sealed class CompleteStagePeriodsCommandHandler(
    AcademicYearResolver yearResolver,
    StagePeriodRunner runner)
    : ICommandHandler<CompleteStagePeriodsCommand, int>
{
    public async Task<Result<int>> Handle(CompleteStagePeriodsCommand request, CancellationToken ct)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, ct);
        return year.IsFailure
            ? Result.Failure<int>(year.Error)
            : await runner.CompleteStageAsync(
                request.StageId, year.Value, request.CohortIds, request.PartitionLabels,
                request.PeriodNumbers, ct);
    }
}
