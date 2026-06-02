using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Planning;

public sealed record CohortProvisionResult(int Created, int Skipped, int MatchedGroups);

/// <summary>
/// Ensures a <see cref="Cohort"/> exists for every academic group of the given
/// partitions in the given stages, idempotently (existing group×stage pairs are
/// skipped). Shared by bulk cohort creation and the macro-plan orchestrator.
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

        var foundStageIds = await dbContext.Stages
            .Where(s => stageIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var missingStageId = stageIds.FirstOrDefault(id => !foundStageIds.Contains(id));
        if (missingStageId != 0)
            return Result.Failure<CohortProvisionResult>(StageErrors.NotFound(missingStageId));

        var groups = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.AcademicYearId == academicYearId
                     && g.RotationGroup != null
                     && partitionKeys.Contains(g.RotationGroup))
            .Select(g => new { g.Id, g.Label, g.RotationGroup })
            .ToListAsync(ct);

        if (groups.Count == 0)
            return Result.Success(new CohortProvisionResult(0, 0, 0));

        var groupIds = groups.Select(g => g.Id).ToList();
        var existingSet = (await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => groupIds.Contains(c.AcademicGroupId) && stageIds.Contains(c.StageId))
            .Select(c => new { c.AcademicGroupId, c.StageId })
            .ToListAsync(ct))
            .Select(p => (p.AcademicGroupId, p.StageId))
            .ToHashSet();

        var groupsByPartition = groups
            .GroupBy(g => g.RotationGroup!)
            .ToDictionary(grp => grp.Key, grp => grp.ToList());

        int created = 0, skipped = 0;
        var newCohorts = new List<Cohort>();

        foreach (var mapping in mappings)
        {
            if (!groupsByPartition.TryGetValue(mapping.RotationGroup, out var partitionGroups))
                continue;

            foreach (var group in partitionGroups)
            {
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

        return Result.Success(new CohortProvisionResult(created, skipped, groups.Count));
    }
}
