using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Schedule.AutoArrange;

internal sealed class AutoArrangeStageScheduleCommandHandler(
    AcademicYearResolver yearResolver,
    RotationArranger arranger)
    : ICommandHandler<AutoArrangeStageScheduleCommand, AutoArrangeResult>
{
    public async Task<Result<AutoArrangeResult>> Handle(
        AutoArrangeStageScheduleCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<AutoArrangeResult>(year.Error);

        var result = await arranger.ArrangeAsync(
            request.StageId,
            year.Value,
            request.PartitionLabels,
            request.PeriodNumbers,
            request.PartitionCount,
            cancellationToken);

        if (result.IsFailure)
            return Result.Failure<AutoArrangeResult>(result.Error);

        var r = result.Value;
        return Result.Success(new AutoArrangeResult(
            r.Assigned, r.SaturatedServices, r.TotalStudents, r.TotalCapacity, r.GroupConflicts));
    }
}
