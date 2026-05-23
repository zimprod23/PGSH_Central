using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.RotationPlan;

internal sealed class CreateRotationPlanCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateRotationPlanCommand, CreateRotationPlanResponse>
{
    public async Task<Result<CreateRotationPlanResponse>> Handle(
        CreateRotationPlanCommand request, CancellationToken cancellationToken)
    {
        var cohorts = await dbContext.Cohorts
            .Where(c => request.CohortIds.Contains(c.Id) && c.StageId == request.StageId)
            .ToListAsync(cancellationToken);

        if (cohorts.Count != request.CohortIds.Distinct().Count())
            return Result.Failure<CreateRotationPlanResponse>(Error.Validation(
                "RotationPlan.CohortNotFound",
                "One or more cohorts were not found in the specified stage."));

        var serviceIds = request.Slots.Select(s => s.ServiceId).Distinct().ToList();
        bool allServicesExist = await dbContext.Services
            .CountAsync(s => serviceIds.Contains(s.Id), cancellationToken) == serviceIds.Count;

        if (!allServicesExist)
            return Result.Failure<CreateRotationPlanResponse>(Error.Validation(
                "RotationPlan.ServiceNotFound",
                "One or more services were not found."));

        // Detach selected cohorts from their previous plan if they had one
        var previousPlanIds = cohorts
            .Where(c => c.RotationPlanId.HasValue)
            .Select(c => c.RotationPlanId!.Value)
            .Distinct()
            .ToList();

        var plan = new Domain.Stages.RotationPlan
        {
            StageId = request.StageId,
        };

        for (int i = 0; i < request.Slots.Count; i++)
        {
            var slot = request.Slots[i];
            plan.Slots.Add(new RotationPlanSlot
            {
                ServiceId     = slot.ServiceId,
                PlannedStart  = slot.StartDate,
                PlannedEnd    = slot.EndDate,
                SequenceOrder = i + 1,
            });
        }

        dbContext.RotationPlans.Add(plan);

        foreach (var cohort in cohorts)
            cohort.RotationPlanId = null; // will be set after SaveChanges assigns plan.Id

        // Flush plan.Id
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var cohort in cohorts)
            cohort.RotationPlanId = plan.Id;

        int? mirrorPlanId = null;

        if (request.AutoMirror)
        {
            var remainingCohorts = await dbContext.Cohorts
                .Where(c => c.StageId == request.StageId && !request.CohortIds.Contains(c.Id))
                .ToListAsync(cancellationToken);

            if (remainingCohorts.Count > 0)
            {
                var mirrorPlan = new Domain.Stages.RotationPlan
                {
                    StageId = request.StageId,
                };

                // Mirror = same dates, services reversed per slot position
                var reversedServiceIds = request.Slots.Select(s => s.ServiceId).Reverse().ToList();
                for (int i = 0; i < request.Slots.Count; i++)
                {
                    var slot = request.Slots[i];
                    mirrorPlan.Slots.Add(new RotationPlanSlot
                    {
                        ServiceId     = reversedServiceIds[i],
                        PlannedStart  = slot.StartDate,
                        PlannedEnd    = slot.EndDate,
                        SequenceOrder = i + 1,
                    });
                }

                dbContext.RotationPlans.Add(mirrorPlan);
                await dbContext.SaveChangesAsync(cancellationToken);

                mirrorPlanId = mirrorPlan.Id;

                foreach (var cohort in remainingCohorts)
                    cohort.RotationPlanId = mirrorPlan.Id;
            }
        }

        // Clean up orphaned plans no longer referenced by any cohort
        if (previousPlanIds.Count > 0)
        {
            var orphanedPlanIds = await dbContext.RotationPlans
                .Where(p => previousPlanIds.Contains(p.Id) && !p.Cohorts.Any())
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            if (orphanedPlanIds.Count > 0)
            {
                var orphans = await dbContext.RotationPlans
                    .Where(p => orphanedPlanIds.Contains(p.Id))
                    .ToListAsync(cancellationToken);
                dbContext.RotationPlans.RemoveRange(orphans);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateRotationPlanResponse(plan.Id, mirrorPlanId));
    }
}
