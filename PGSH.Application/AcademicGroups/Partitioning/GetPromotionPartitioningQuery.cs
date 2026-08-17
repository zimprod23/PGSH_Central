using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicGroups.AssignRotationGroups;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Repartition;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Partitioning;

/// <summary>
/// How one promotion is currently divided: each partition's membership, and the rosters that carry no
/// partition at all.
///
/// <para>⚠ <b>This exists because a count must not be read off a page.</b> The Plan macro tab derived
/// the partitions, their sizes and « N groupes sans partition » from <c>GET /groups</c> at
/// <c>pageSize: 200</c>. A promotion adds ~100 rosters a year, so past 200 the tab would have silently
/// shown a partition as smaller than it is and under-reported the unlabelled rosters — the very number
/// that tells an admin a gap-fill is owed. Raising the page size moves the cliff; it does not remove
/// it. The aggregate is computed where the rows are.</para>
///
/// <para>The promotion is (année, niveau) and the level is required, so « Non réparti » — which carries
/// no level — is excluded by construction, as everywhere else partitions are concerned.</para>
/// </summary>
public sealed record GetPromotionPartitioningQuery(int LevelId, int? AcademicYearId = null)
    : IQuery<PromotionPartitioningResponse>;

/// <param name="UnlabelledGroupNumbers">
/// Collapsed the way the répartition prints a cell (<c>"3, 12, 21"</c>, <c>"41-60"</c>) — an admin has
/// to recognise <i>which</i> rosters these are, not merely how many.
/// </param>
public sealed record PromotionPartitioningResponse(
    int AcademicYearId,
    int TotalGroups,
    int LabelledGroups,
    int UnlabelledGroups,
    string UnlabelledGroupNumbers,
    IReadOnlyList<PartitionMembership> Partitions);

internal sealed class GetPromotionPartitioningQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetPromotionPartitioningQuery, PromotionPartitioningResponse>
{
    public async Task<Result<PromotionPartitioningResponse>> Handle(
        GetPromotionPartitioningQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<PromotionPartitioningResponse>(year.Error);

        // Two integers per roster and nothing else: no registrations, no cohorts, no correlated
        // sub-queries. Deliberately unpaged — the answer is a count over the whole promotion, and a
        // page of it would be the defect this query replaces.
        var rosters = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.AcademicYearId == year.Value && g.LevelId == request.LevelId)
            .Select(g => new { g.GroupNumber, g.RotationGroup })
            .ToListAsync(cancellationToken);

        var partitions = rosters
            .Where(g => g.RotationGroup != null)
            .GroupBy(g => g.RotationGroup!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PartitionMembership(
                g.Key,
                g.Count(),
                GroupNumberRanges.Format(g.Select(x => x.GroupNumber))))
            .ToList();

        var unlabelled = rosters.Where(g => g.RotationGroup == null).ToList();

        return new PromotionPartitioningResponse(
            year.Value,
            rosters.Count,
            rosters.Count - unlabelled.Count,
            unlabelled.Count,
            GroupNumberRanges.Format(unlabelled.Select(g => g.GroupNumber)),
            partitions);
    }
}
