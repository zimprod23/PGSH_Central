using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.Create;

internal sealed class CreateCohortCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateCohortCommand, int>
{
    public async Task<Result<int>> Handle(CreateCohortCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => new { s.Name, s.LevelId, LevelLabel = s.Level == null ? null : s.Level.Label })
            .FirstOrDefaultAsync(cancellationToken);
        if (stage is null)
            return Result.Failure<int>(StageErrors.NotFound(request.StageId));

        var group = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == request.AcademicGroupId)
            .Select(g => new { g.Label, g.LevelId, LevelLabel = g.Level == null ? null : g.Level.Label })
            .FirstOrDefaultAsync(cancellationToken);
        if (group is null)
            return Result.Failure<int>(AcademicGroupErrors.NotFound(request.AcademicGroupId));

        // Both ends of a cohorte belong to one promotion. CohortProvisioner enforces this on the bulk
        // path; without it here the hand-built path could pair any roster with any stage, which is the
        // shape of defect SplitAcademicGroupsPerLevel had to undo across 1,003 rows.
        if (group.LevelId is null)
            return Result.Failure<int>(StageErrors.CohortOnUnassignedRoster(group.Label));

        if (group.LevelId != stage.LevelId)
            return Result.Failure<int>(StageErrors.CohortPromotionMismatch(
                group.Label,
                group.LevelLabel ?? $"niveau {group.LevelId}",
                stage.Name,
                stage.LevelLabel ?? $"niveau {stage.LevelId}"));

        bool duplicate = await dbContext.Cohorts
            .AnyAsync(c => c.StageId == request.StageId && c.AcademicGroupId == request.AcademicGroupId, cancellationToken);
        if (duplicate)
            return Result.Failure<int>(Error.Conflict(
                "Cohorts.Duplicate",
                "A cohort for this group and stage already exists."));

        var cohort = new Cohort
        {
            StageId         = request.StageId,
            AcademicGroupId = request.AcademicGroupId,
            Label           = request.Label,
        };

        dbContext.Cohorts.Add(cohort);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(cohort.Id);
    }
}
