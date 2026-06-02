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
                var affected = await affectation.AssignByStageAsync(plan.StageId, partition, cancellationToken);
                studentsAssigned += affected.SuccessCount;
            }

            if (request.AutoArrange)
            {
                var arranged = await arranger.ArrangeAsync(
                    plan.StageId, partition, plan.PeriodNumbers, null, cancellationToken);
                if (arranged.IsFailure)
                    return Result.Failure<MacroPlanResult>(arranged.Error);

                cellsArranged += arranged.Value.Assigned;
                saturated     += arranged.Value.SaturatedServices;
            }
        }

        if (request.Publish)
        {
            foreach (var plan in request.Plans)
            {
                var published = await publisher.PublishStageAsync(
                    plan.StageId, [plan.RotationGroup], plan.PeriodNumbers, cancellationToken);
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
            periodsPublished));
    }
}
