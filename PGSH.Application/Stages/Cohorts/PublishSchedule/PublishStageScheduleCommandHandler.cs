using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

internal sealed class PublishStageScheduleCommandHandler(
    AcademicYearResolver yearResolver,
    SchedulePublisher publisher)
    : ICommandHandler<PublishStageScheduleCommand, PublishResult>
{
    public async Task<Result<PublishResult>> Handle(
        PublishStageScheduleCommand request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<PublishResult>(year.Error);

        return await publisher.PublishStageAsync(
            request.StageId, year.Value, request.PartitionLabels, request.PeriodNumbers,
            request.AllowOverCapacity, cancellationToken);
    }
}
