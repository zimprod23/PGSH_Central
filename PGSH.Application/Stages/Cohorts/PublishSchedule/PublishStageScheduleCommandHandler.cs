using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

internal sealed class PublishStageScheduleCommandHandler(SchedulePublisher publisher)
    : ICommandHandler<PublishStageScheduleCommand, PublishResult>
{
    public Task<Result<PublishResult>> Handle(PublishStageScheduleCommand request, CancellationToken cancellationToken)
        => publisher.PublishStageAsync(request.StageId, request.PartitionLabels, request.PeriodNumbers, request.AllowOverCapacity, cancellationToken);
}
