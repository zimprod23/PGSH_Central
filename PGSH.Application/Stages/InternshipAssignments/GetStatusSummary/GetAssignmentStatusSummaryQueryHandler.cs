using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetStatusSummary;

internal sealed class GetAssignmentStatusSummaryQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetAssignmentStatusSummaryQuery, List<AssignmentStatusCount>>
{
    public async Task<Result<List<AssignmentStatusCount>>> Handle(
        GetAssignmentStatusSummaryQuery request, CancellationToken cancellationToken)
    {
        var assignments = dbContext.InternshipAssignments.AsNoTracking().AsQueryable();

        if (request.CohortIds is { Count: > 0 })
            assignments = assignments.Where(a => request.CohortIds.Contains(a.CurrentCohortId));

        if (request.StageId.HasValue)
            assignments = assignments.Where(a => a.Cohort.StageId == request.StageId.Value);

        // Count per ROTATION (ServicePeriod), not per assignment: once a period is closed the
        // card must show it as à-évaluer/évalué even while the assignment stays Ongoing (the
        // domain only flips to Completed when ALL periods close). Terminal admin verdicts on the
        // assignment (Validated/Rejected) still override the period state.
        var periods = assignments.SelectMany(a => a.ServicePeriods.Select(p => new
        {
            a.Status,
            p.IsStarted,
            p.IsComplete,
            HasEvaluation = p.Evaluation != null,
            PeriodNumber  = p.CohortSlotAssignment != null
                ? (int?)p.CohortSlotAssignment.StageSlot.PeriodNumber
                : null,
        }));

        if (request.PeriodNumbers is { Count: > 0 } periodNumbers)
            periods = periods.Where(x => x.PeriodNumber != null && periodNumbers.Contains(x.PeriodNumber.Value));

        var rows = await periods.ToListAsync(cancellationToken);

        var counts = rows
            .GroupBy(r =>
                r.Status == InternshipStatus.Rejected  ? InternshipStatus.Rejected  :
                r.Status == InternshipStatus.Validated ? InternshipStatus.Validated :
                r.IsComplete && r.HasEvaluation        ? InternshipStatus.Evaluated  :
                r.IsComplete                           ? InternshipStatus.Completed  :
                r.IsStarted                            ? InternshipStatus.Ongoing    :
                                                         InternshipStatus.Planned)
            .Select(g => new AssignmentStatusCount(g.Key, g.Count()))
            .ToList();

        return Result.Success(counts);
    }
}
