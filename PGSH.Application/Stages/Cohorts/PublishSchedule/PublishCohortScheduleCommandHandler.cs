using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.PublishSchedule;

internal sealed class PublishCohortScheduleCommandHandler(SchedulePublisher publisher)
    : ICommandHandler<PublishCohortScheduleCommand>
{
    public Task<Result> Handle(PublishCohortScheduleCommand request, CancellationToken cancellationToken)
        => publisher.PublishCohortAsync(request.CohortId, request.AllowOverCapacity, cancellationToken);
}
