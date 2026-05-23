using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.RotationPlan;

public sealed record CreateRotationPlanCommand(
    int StageId,
    List<int> CohortIds,
    List<RotationSlotRequest> Slots,
    bool AutoMirror) : ICommand<CreateRotationPlanResponse>;

public sealed record RotationSlotRequest(
    int ServiceId,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record CreateRotationPlanResponse(
    int PlanId,
    int? MirrorPlanId);
