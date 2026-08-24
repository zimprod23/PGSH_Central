using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

public sealed record CohortProvisionResult(
    int Created,
    int Skipped,
    int MatchedGroups,
    /// <summary>
    /// Group×stage pairs refused because the group's CNPN does not require that stage of its level.
    /// Reported rather than silently dropped: it usually means the wrong row was ticked in the
    /// matrix, and a plan that quietly omits a partition is worse than one that says why.
    /// </summary>
    int NotRequiredByCnpn = 0);

/// <summary>
/// Ensures a <see cref="Cohort"/> exists for every academic group of the given
/// partitions in the given stages, idempotently (existing group×stage pairs are
/// skipped). Shared by bulk cohort creation and the macro-plan orchestrator.
///
/// <para><b>The CNPN is a guardrail here, not just documentation.</b> A group is only given a cohort
/// for a stage its own text requires of its level. Since arrêté 1650.25 one level holds students of
/// two texts owing different stage sets, so "this stage belongs to this level" is no longer enough —
/// it has to be "this stage belongs to this level <i>under the text these students follow</i>".</para>
///
/// <para>Where no requirement set is recorded for a (text, level) the check stands aside and the
/// cohort is created. Refusing would be worse than useless: arrêté 1650.25's requirements have not
/// been entered yet, so an enforcing check would block all planning for six-year students on the
/// strength of data nobody has typed in.</para>
/// </summary>
internal sealed class CohortProvisioner(IApplicationDbContext dbContext)
{
    public async Task<Result<CohortProvisionResult>> EnsureCohortsAsync(
        int academicYearId,
        IReadOnlyList<(string RotationGroup, int StageId)> mappings,
        CancellationToken ct)
    {
        var stageIds      = mappings.Select(m => m.StageId).Distinct().ToList();
        var partitionKeys = mappings.Select(m => m.RotationGroup).Distinct().ToList();

        var stages = await dbContext.Stages
            .Where(s => stageIds.Contains(s.Id))
            .Select(s => new { s.Id, s.LevelId })
            .ToListAsync(ct);

        var missingStageId = stageIds.FirstOrDefault(id => stages.All(s => s.Id != id));
        if (missingStageId != 0)
            return Result.Failure<CohortProvisionResult>(StageErrors.NotFound(missingStageId));

        var stageLevel = stages.ToDictionary(s => s.Id, s => s.LevelId);
        var levelIds   = stageLevel.Values.Distinct().ToList();

        // A group belongs to a stage's level by LevelId and by nothing else. Scoping here prevents a
        // partition label that is reused across levels (A in 1Med and A in 2Med) from creating
        // cross-level cohorts.
        //
        // ⚠ There used to be a second reach — "or has a registration at that level" — for legacy
        // rosters that carried no LevelId. SplitAcademicGroupsPerLevel gave every real roster its
        // promotion, so the only rows left without one are the year's « Non réparti » buckets, and a
        // bucket holds every promotion at once: the fallback would have matched it for *every* level
        // in the plan and given 4,725 people a cohorte. Level-less rosters are excluded outright, and
        // AcademicGroupErrors refuses the two acts that could label one.
        var groups = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.AcademicYearId == academicYearId
                     && g.LevelId != null
                     && levelIds.Contains(g.LevelId.Value)
                     && g.RotationGroup != null
                     && partitionKeys.Contains(g.RotationGroup))
            .Select(g => new
            {
                g.Id,
                g.Label,
                g.RotationGroup,
                g.LevelId,
                // Auto-arrange keeps a group to one text; a hand-built one might not, so take the
                // distinct set and only trust it when there is exactly one.
                //
                // ⚠ The registration's stamp first, the student's only as a fallback. A group is a
                // roster of one year, so what its members owe is what the text governing *that* year
                // required — which is exactly what the registration records and what the student's
                // own stamp stops being the moment an effectivity rule moves him. Null on both is the
                // legacy row nobody has resolved; it simply does not join the set.
                CnpnVersionIds = g.Registrations
                    .Where(r => r.CnpnVersionId != null || r.Student.CnpnVersionId != null)
                    .Select(r => r.CnpnVersionId ?? r.Student.CnpnVersionId!.Value)
                    .Distinct()
                    .ToList(),
            })
            .ToListAsync(ct);

        if (groups.Count == 0)
            return Result.Success(new CohortProvisionResult(0, 0, 0));

        // (text, level) → the stages it requires. Absent from this map means nothing was recorded for
        // that pair, which is the "stand aside" case rather than "requires nothing".
        var required = (await dbContext.Curriculums
                .AsNoTracking()
                .Where(c => levelIds.Contains(c.LevelId))
                .Select(c => new
                {
                    c.CnpnVersionId,
                    c.LevelId,
                    StageIds = c.Stages.Select(s => s.StageId).ToList(),
                })
                .ToListAsync(ct))
            .ToDictionary(c => (c.CnpnVersionId, c.LevelId), c => c.StageIds.ToHashSet());

        var groupIds = groups.Select(g => g.Id).ToList();
        var existingSet = (await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => groupIds.Contains(c.AcademicGroupId) && stageIds.Contains(c.StageId))
            .Select(c => new { c.AcademicGroupId, c.StageId })
            .ToListAsync(ct))
            .Select(p => (p.AcademicGroupId, p.StageId))
            .ToHashSet();

        int created = 0, skipped = 0, notRequired = 0;
        var newCohorts = new List<Cohort>();

        foreach (var mapping in mappings)
        {
            int level = stageLevel[mapping.StageId];

            var partitionGroups = groups.Where(g =>
                g.RotationGroup == mapping.RotationGroup && g.LevelId == level);

            foreach (var group in partitionGroups)
            {
                // Only a group settled on one text can be checked against it. A mixed or unstamped
                // group is left to the existing level rule — flagging it here would report the same
                // problem twice, and auto-arrange is where it gets prevented.
                if (group.CnpnVersionIds.Count == 1
                    && required.TryGetValue((group.CnpnVersionIds[0], level), out var stagesOfText)
                    && !stagesOfText.Contains(mapping.StageId))
                {
                    notRequired++;
                    continue;
                }

                if (!existingSet.Add((group.Id, mapping.StageId)))
                {
                    skipped++;
                    continue;
                }

                newCohorts.Add(new Cohort
                {
                    StageId         = mapping.StageId,
                    AcademicGroupId = group.Id,
                    Label           = group.Label,
                });
                created++;
            }
        }

        if (newCohorts.Count > 0)
        {
            await dbContext.Cohorts.AddRangeAsync(newCohorts, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        return Result.Success(new CohortProvisionResult(created, skipped, groups.Count, notRequired));
    }
}
