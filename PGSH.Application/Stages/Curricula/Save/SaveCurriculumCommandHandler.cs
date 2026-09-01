using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Curricula.Save;

internal sealed class SaveCurriculumCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<SaveCurriculumCommand, int>
{
    public async Task<Result<int>> Handle(SaveCurriculumCommand request, CancellationToken cancellationToken)
    {
        // The CNPN decides what every student of a level owes; recording one is scolarité business.
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure)
            return Result.Failure<int>(access.Error);

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<int>(Error.NotFound(
                "Levels.NotFound", $"Level '{request.LevelId}' not found."));

        var version = await dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => v.Id == request.CnpnVersionId)
            .Select(v => new { v.TotalYears, v.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
            return Result.Failure<int>(CurriculumErrors.VersionNotFound(request.CnpnVersionId));

        if (version.AcademicProgram != level.AcademicProgram)
            return Result.Failure<int>(CurriculumErrors.ProgramMismatch);

        // The point of recording TotalYears: a six-year text has no seventh year, and requiring
        // stages of a level outside its span would produce an obligation nobody can ever serve.
        if (level.Year > version.TotalYears)
            return Result.Failure<int>(
                CurriculumErrors.LevelOutsideProgramme(level.Year, version.TotalYears));

        // A CNPN lists the stages of its own level. Letting another level's stage in would make the
        // requirement unservable — no cohort of this level would ever run it.
        var stageIds = request.Stages.Select(s => s.StageId).ToList();
        var foreignStage = await dbContext.Stages
            .AsNoTracking()
            .Where(s => stageIds.Contains(s.Id) && s.LevelId != request.LevelId)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (foreignStage is not null)
            return Result.Failure<int>(CurriculumErrors.StageNotInLevel(foreignStage.Value, request.LevelId));

        int knownStages = await dbContext.Stages
            .CountAsync(s => stageIds.Contains(s.Id), cancellationToken);

        if (knownStages != stageIds.Count)
            return Result.Failure<int>(Error.NotFound(
                "Stages.NotFound", "Un ou plusieurs stages du CNPN n'existent pas."));

        var curriculum = await dbContext.Curriculums
            .Include(c => c.Stages)
            .FirstOrDefaultAsync(
                c => c.LevelId == request.LevelId && c.CnpnVersionId == request.CnpnVersionId,
                cancellationToken);

        if (curriculum is null)
        {
            curriculum = new Curriculum
            {
                LevelId       = request.LevelId,
                CnpnVersionId = request.CnpnVersionId,
            };
            dbContext.Curriculums.Add(curriculum);
        }

        curriculum.Reference = request.Reference;

        // Reconcile against what is stored so a dropped stage goes through RemoveStage and announces
        // itself — students who failed it still owe it, and that has to be visible.
        var wanted = request.Stages.ToDictionary(s => s.StageId);

        foreach (int removedStageId in curriculum.Stages.Select(s => s.StageId).Except(wanted.Keys).ToList())
        {
            var removal = curriculum.RemoveStage(removedStageId);
            if (removal.IsFailure) return Result.Failure<int>(removal.Error);
        }

        foreach (var (stageId, input) in wanted)
        {
            var existing = curriculum.Stages.FirstOrDefault(s => s.StageId == stageId);
            if (existing is null)
            {
                var added = curriculum.AddStage(stageId, input.Coefficient, input.DurationInDays);
                if (added.IsFailure) return Result.Failure<int>(added.Error);
                continue;
            }

            // A text can keep a stage and reweight it; that is an amendment, not a removal.
            existing.Coefficient    = input.Coefficient;
            existing.DurationInDays = input.DurationInDays;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return curriculum.Id;
    }
}
