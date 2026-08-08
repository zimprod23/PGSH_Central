using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.MacroPlan;

internal sealed class GenerateMacroPlanCommandHandler(
    CohortProvisioner provisioner,
    StudentAffectationService affectation,
    RotationArranger arranger,
    SchedulePublisher publisher)
    : ICommandHandler<GenerateMacroPlanCommand, MacroPlanResult>
{
    public async Task<Result<MacroPlanResult>> Handle(
        GenerateMacroPlanCommand request, CancellationToken cancellationToken)
    {
        var cohortResult = await provisioner.EnsureCohortsAsync(
            request.AcademicYearId,
            request.Plans.Select(p => (p.RotationGroup, p.StageId)).ToList(),
            cancellationToken);

        if (cohortResult.IsFailure)
            return Result.Failure<MacroPlanResult>(cohortResult.Error);

        int studentsAssigned = 0, cellsArranged = 0, saturated = 0, cohortsPublished = 0, periodsPublished = 0;

        foreach (var plan in request.Plans)
        {
            string[] partition = [plan.RotationGroup];

            if (request.AssignStudents)
            {
                var affected = await affectation.AssignByStageAsync(
                    plan.StageId, request.AcademicYearId, partition, cancellationToken);
                studentsAssigned += affected.SuccessCount;
            }

            if (request.AutoArrange)
            {
                var arranged = await arranger.ArrangeAsync(
                    plan.StageId, request.AcademicYearId, partition, plan.PeriodNumbers, null, cancellationToken);

                // A stage whose period slots aren't defined yet is a setup-order issue,
                // not a hard error: keep the cohorts/affectation already done and let the
                // admin define slots then re-run. Other failures still surface.
                if (arranged.IsFailure)
                {
                    if (arranged.Error.Code == "Schedule.NoSlots") continue;
                    return Result.Failure<MacroPlanResult>(arranged.Error);
                }

                cellsArranged += arranged.Value.Assigned;
                saturated     += arranged.Value.SaturatedServices;
            }
        }

        if (request.Publish)
        {
            foreach (var plan in request.Plans)
            {
                var published = await publisher.PublishStageAsync(
                    plan.StageId, request.AcademicYearId, [plan.RotationGroup], plan.PeriodNumbers,
                    request.AllowOverCapacity, cancellationToken);
                if (published.IsFailure)
                    return Result.Failure<MacroPlanResult>(published.Error);

                cohortsPublished += published.Value.PublishedCohorts;
                periodsPublished += published.Value.PeriodsCreated;
            }
        }

        return Result.Success(new MacroPlanResult(
            cohortResult.Value.Created,
            cohortResult.Value.Skipped,
            studentsAssigned,
            cellsArranged,
            saturated,
            cohortsPublished,
            periodsPublished,
            cohortResult.Value.NotRequiredByCnpn));
    }
}
