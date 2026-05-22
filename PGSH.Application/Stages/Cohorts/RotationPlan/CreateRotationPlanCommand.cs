using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.RotationPlan;

public sealed record CreateRotationPlanCommand(
    List<int>               CohortIds,
    List<int>               ServiceIds,
    List<RotationSlotRequest> Slots) : ICommand<BulkResponse<int, int>>;

public sealed record RotationSlotRequest(
    int      SequenceOrder,
    DateOnly StartDate,
    DateOnly EndDate);
