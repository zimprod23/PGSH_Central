using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Application.Stages.Cohorts.GetById;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.GetByStage;

internal sealed class GetCohortByStageIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetCohortsByStageQuery, PaginatedResponse<CohortResponse>>
{
    public async Task<Result<PaginatedResponse<CohortResponse>>> Handle(
        GetCohortsByStageQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == request.StageId);

        if (request.AcademicYearId.HasValue)
            query = query.Where(c => c.AcademicGroup.AcademicYearId == request.AcademicYearId.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(c =>
                c.Label.ToLower().Contains(term)
             || c.AcademicGroup.Label.ToLower().Contains(term));
        }

        var page = await query
            .OrderBy(c => c.AcademicGroup.GroupNumber)
            .ThenBy(c => c.Id)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                c => new CohortRow(
                    c.Id,
                    c.StageId,
                    c.Stage.Name,
                    c.AcademicGroupId,
                    c.AcademicGroup.Label,
                    c.Label,
                    c.Assignments.Count,
                    c.SlotAssignments.Count,
                    c.Assignments.Any(a => a.ServicePeriods.Any(p => p.CohortSlotAssignmentId != null)),
                    c.AcademicGroup.AcademicYearId,
                    c.AcademicGroup.AcademicYear.Label,
                    c.AcademicGroup.RotationGroup),
                cancellationToken);

        // ⚠ A second flat query, not a collection subquery in the projection above. The element
        // would be a computed int carrying no key — the exact shape Npgsql refuses, and the one that
        // took the macro plan down with the whole suite green. Keyed on the page's ids, so it costs
        // one round trip whatever the promotion's size.
        var periodsByCohort = (await PeriodNumbersQuery(dbContext, page.Items.Select(c => c.Id).ToList())
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.CohortId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(p => p.PeriodNumber).Order().ToList());

        return new PaginatedResponse<CohortResponse>(
            page.Items.Select(c => new CohortResponse(
                c.Id, c.StageId, c.StageName, c.AcademicGroupId, c.AcademicGroupLabel, c.Label,
                c.StudentAssignmentCount, c.SlotAssignmentCount, c.IsSchedulePublished,
                c.AcademicYearId, c.AcademicYearLabel, c.RotationGroup,
                periodsByCohort.GetValueOrDefault(c.Id, []))).ToList(),
            page.PageNumber, page.PageSize, page.TotalCount);
    }

    /// <summary>Which column of the axis each of these cohortes stands in.</summary>
    /// <remarks>⚠ Named so <c>SqlTranslationTests</c> can compile it — see the call site.</remarks>
    internal static IQueryable<CohortPeriod> PeriodNumbersQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds) =>
        dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => cohortIds.Contains(a.CohortId))
            .Select(a => new CohortPeriod(a.CohortId, a.StageSlot.PeriodNumber))
            .Distinct();

    /// <summary>One cohorte standing in one column.</summary>
    internal sealed record CohortPeriod(int CohortId, int PeriodNumber);

    /// <summary>The row as the store gives it, before its columns are folded in.</summary>
    private sealed record CohortRow(
        int Id, int StageId, string StageName, int AcademicGroupId, string AcademicGroupLabel,
        string Label, int StudentAssignmentCount, int SlotAssignmentCount, bool IsSchedulePublished,
        int AcademicYearId, string AcademicYearLabel, string? RotationGroup);
}
