using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.BulkCreate;

internal sealed class BulkCreateCohortsFromPartitionsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<BulkCreateCohortsFromPartitionsCommand, BulkCohortsFromPartitionsResult>
{
    public async Task<Result<BulkCohortsFromPartitionsResult>> Handle(
        BulkCreateCohortsFromPartitionsCommand request, CancellationToken cancellationToken)
    {
        var stageIds      = request.Mappings.Select(m => m.StageId).Distinct().ToList();
        var partitionKeys = request.Mappings.Select(m => m.RotationGroup).Distinct().ToList();

        var foundStageIds = await dbContext.Stages
            .Where(s => stageIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var missingStageId = stageIds.FirstOrDefault(id => !foundStageIds.Contains(id));
        if (missingStageId != 0)
            return Result.Failure<BulkCohortsFromPartitionsResult>(StageErrors.NotFound(missingStageId));

        var groups = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.AcademicYearId == request.AcademicYearId
                     && g.RotationGroup != null
                     && partitionKeys.Contains(g.RotationGroup))
            .Select(g => new { g.Id, g.Label, g.RotationGroup })
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
            return Result.Failure<BulkCohortsFromPartitionsResult>(
                Error.Validation("MacroPlan.NoPartitionedGroups",
                    "No groups with rotation labels found. Run stage auto-arrange first to assign partition labels to groups."));

        var groupIds = groups.Select(g => g.Id).ToList();
        var existingSet = (await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => groupIds.Contains(c.AcademicGroupId) && stageIds.Contains(c.StageId))
            .Select(c => new { c.AcademicGroupId, c.StageId })
            .ToListAsync(cancellationToken))
            .Select(p => (p.AcademicGroupId, p.StageId))
            .ToHashSet();

        var groupLookup = groups
            .GroupBy(g => g.RotationGroup!)
            .ToDictionary(grp => grp.Key, grp => grp.ToList());

        int created = 0, skipped = 0;
        var newCohorts = new List<Cohort>();

        foreach (var mapping in request.Mappings)
        {
            if (!groupLookup.TryGetValue(mapping.RotationGroup, out var partitionGroups))
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
            await dbContext.Cohorts.AddRangeAsync(newCohorts, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new BulkCohortsFromPartitionsResult(created, skipped));
    }
}
