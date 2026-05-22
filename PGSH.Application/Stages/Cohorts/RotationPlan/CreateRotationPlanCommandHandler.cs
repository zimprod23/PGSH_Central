using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.RotationPlan;

internal sealed class CreateRotationPlanCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateRotationPlanCommand, BulkResponse<int, int>>
{
    public async Task<Result<BulkResponse<int, int>>> Handle(
        CreateRotationPlanCommand request, CancellationToken cancellationToken)
    {
        var existingCohortIds = await dbContext.Cohorts
            .Where(c => request.CohortIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToHashSetAsync(cancellationToken);

        bool allServicesExist = await dbContext.Services
            .CountAsync(s => request.ServiceIds.Contains(s.Id), cancellationToken)
            == request.ServiceIds.Count;

        if (!allServicesExist)
            return Result.Failure<BulkResponse<int, int>>(Error.Validation(
                "RotationPlan.ServiceNotFound",
                "One or more services were not found."));

        // Replace existing templates for the selected cohorts
        var toDelete = await dbContext.CohortRotationTemplates
            .Where(t => request.CohortIds.Contains(t.CohortId))
            .ToListAsync(cancellationToken);

        dbContext.CohortRotationTemplates.RemoveRange(toDelete);

        var results   = new List<BulkItemResult<int, int>>();
        int svcCount  = request.ServiceIds.Count;

        for (int i = 0; i < request.CohortIds.Count; i++)
        {
            int cohortId = request.CohortIds[i];

            if (!existingCohortIds.Contains(cohortId))
            {
                results.Add(new BulkItemResult<int, int>(cohortId, 0,
                    Error.NotFound("Cohorts.NotFound", $"Cohort {cohortId} not found.")));
                continue;
            }

            foreach (var slot in request.Slots)
            {
                // Cyclic distribution: cohort i gets service offset by i positions
                int serviceId = request.ServiceIds[(i + slot.SequenceOrder - 1) % svcCount];

                dbContext.CohortRotationTemplates.Add(new CohortRotationTemplate
                {
                    CohortId      = cohortId,
                    ServiceId     = serviceId,
                    PlannedStart  = slot.StartDate,
                    PlannedEnd    = slot.EndDate,
                    SequenceOrder = slot.SequenceOrder,
                });
            }

            results.Add(new BulkItemResult<int, int>(cohortId, cohortId, null));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResponse<int, int>(
            results,
            request.CohortIds.Count,
            results.Count(r => r.IsSuccess),
            results.Count(r => !r.IsSuccess)));
    }
}
