using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// Reads how a <b>promotion</b> — one (année, niveau) — is cut into rotation partitions.
///
/// <para>⚠ <b>A partition count is a fact about a promotion, never about one stage's cohorts.</b> The
/// arranger used to infer it from the cohorts of the stage it was arranging, and
/// <see cref="PartitionAllocator.BuildLabels"/> takes "the existing count" from whatever labels it is
/// shown. A stage that reaches only part of the promotion — the normal case, since
/// <c>CohortProvisioner</c> skips a stage a text does not require, and cohorts are provisioned stage by
/// stage — therefore showed a promotion cut into ten as a promotion cut into however many labels
/// happened to appear among its own cohorts. The gap-fill then wrote those labels onto real rosters,
/// permanently, and the crossover built on them is nonsense: measured on Med6 (2026-08-13),
/// A = 42, B = 42 and C–J = 2 each on a promotion that had been re-cut into ten clean partitions.
/// The balance counts are wrong for the same reason — "fill the smallest partition" measured against a
/// subset is not the promotion's smallest partition.</para>
///
/// <para>The bucket is excluded by construction: « Non réparti » carries no <c>LevelId</c>, so it can
/// never match a promotion's. See <c>AcademicGroupErrors.UnassignedRosterCannotBePartitioned</c>.</para>
/// </summary>
internal sealed class PromotionPartitioning(IApplicationDbContext dbContext)
{
    public async Task<PromotionCut> ReadAsync(
        int academicYearId, int levelId, CancellationToken cancellationToken)
    {
        var rosters = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.AcademicYearId == academicYearId && g.LevelId == levelId)
            .OrderBy(g => g.GroupNumber)
            .Select(g => new RosterLabel(g.Id, g.RotationGroup))
            .ToListAsync(cancellationToken);

        return new PromotionCut(rosters);
    }
}

internal sealed record RosterLabel(int GroupId, string? Label);

/// <summary>
/// One promotion's rosters in group-number order, with whatever partition each currently carries.
/// </summary>
internal sealed record PromotionCut(IReadOnlyList<RosterLabel> Rosters)
{
    /// <summary>
    /// Whether anyone has divided this promotion. Genuinely promotion-wide: a stage whose own cohorts
    /// are all unlabelled says nothing about it.
    /// </summary>
    public bool IsCut => Rosters.Any(r => r.Label is not null);

    /// <summary>
    /// Labels for the rosters that carry none, balanced against <b>the whole promotion</b>. Existing
    /// labels are never moved — see <see cref="PartitionAllocator.AssignUnlabelled"/>.
    /// </summary>
    public Dictionary<int, string> FillGaps(int requestedCount) =>
        PartitionAllocator.AssignUnlabelled(
            Rosters.Select(r => (r.GroupId, r.Label)).ToList(), requestedCount);
}
