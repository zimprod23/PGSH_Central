using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Manage;

/// <summary>
/// « 1650.25 reprend 2174.18 » — seeds every level of one text from another's in a single act.
///
/// <para>This is how an arrêté actually reads: the previous text, except for the years it changes.
/// Cloning level by level made a six-year programme six separate actions, with nothing showing which
/// levels had been done — so the realistic flow was also the easiest one to leave half-finished.
/// Clone the whole text, then edit the two or three years the new arrêté actually touches.</para>
///
/// <para>Levels the target already carries are left untouched, so re-running after a hand edit never
/// overwrites it. Levels beyond the target's span are skipped and counted: a six-year text has no
/// seventh year, and that is the whole point of recording <c>TotalYears</c>.</para>
/// </summary>
public sealed record CloneCnpnCurriculaCommand(int FromCnpnVersionId, int ToCnpnVersionId)
    : ICommand<CnpnCloneResult>, IAuditableCommand
{
    public string  AuditAction     => "CNPN_CURRICULA_CLONED";
    public string  AuditEntityType => "CnpnVersion";
    public string? AuditEntityId   => ToCnpnVersionId.ToString();
    public string? AuditMetadata   => $$"""{"fromCnpnVersionId":{{FromCnpnVersionId}}}""";
}

public sealed record CnpnCloneResult(
    int LevelsCloned,
    int StagesCopied,
    /// <summary>Levels the target already had; left exactly as they were.</summary>
    int LevelsSkipped,
    /// <summary>Levels the source has that fall outside the target's span — a 7th year onto 6 years.</summary>
    int LevelsOutsideProgramme);

internal sealed class CloneCnpnCurriculaCommandValidator : AbstractValidator<CloneCnpnCurriculaCommand>
{
    public CloneCnpnCurriculaCommandValidator()
    {
        RuleFor(x => x.FromCnpnVersionId).GreaterThan(0);
        RuleFor(x => x.ToCnpnVersionId).GreaterThan(0);
    }
}

internal sealed class CloneCnpnCurriculaCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CloneCnpnCurriculaCommand, CnpnCloneResult>
{
    public async Task<Result<CnpnCloneResult>> Handle(
        CloneCnpnCurriculaCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<CnpnCloneResult>(access.Error);

        if (request.FromCnpnVersionId == request.ToCnpnVersionId)
            return Result.Failure<CnpnCloneResult>(CnpnErrors.CloneIntoItself);

        var texts = await dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => v.Id == request.FromCnpnVersionId || v.Id == request.ToCnpnVersionId)
            .Select(v => new { v.Id, v.Code, v.AcademicProgram, v.TotalYears })
            .ToListAsync(ct);

        var source = texts.FirstOrDefault(v => v.Id == request.FromCnpnVersionId);
        var target = texts.FirstOrDefault(v => v.Id == request.ToCnpnVersionId);

        if (source is null) return Result.Failure<CnpnCloneResult>(
            CnpnErrors.VersionNotFound(request.FromCnpnVersionId));
        if (target is null) return Result.Failure<CnpnCloneResult>(
            CnpnErrors.VersionNotFound(request.ToCnpnVersionId));

        if (source.AcademicProgram != target.AcademicProgram)
            return Result.Failure<CnpnCloneResult>(
                CnpnErrors.CloneProgramMismatch(source.Code, target.Code));

        var sourceSets = await dbContext.Curriculums
            .AsNoTracking()
            .Include(c => c.Stages)
            .Where(c => c.CnpnVersionId == request.FromCnpnVersionId)
            .Select(c => new
            {
                c.LevelId,
                LevelYear = c.Level.Year,
                c.Reference,
                Stages = c.Stages
                    .Select(s => new { s.StageId, s.Coefficient, s.DurationInDays })
                    .ToList(),
            })
            .ToListAsync(ct);

        if (sourceSets.Count == 0)
            return Result.Failure<CnpnCloneResult>(CnpnErrors.CloneSourceEmpty);

        var alreadyThere = await dbContext.Curriculums
            .Where(c => c.CnpnVersionId == request.ToCnpnVersionId)
            .Select(c => c.LevelId)
            .ToListAsync(ct);

        int cloned = 0, stages = 0, skipped = 0, outside = 0;

        foreach (var set in sourceSets.OrderBy(s => s.LevelYear))
        {
            if (alreadyThere.Contains(set.LevelId)) { skipped++; continue; }
            if (set.LevelYear > target.TotalYears)  { outside++; continue; }

            var curriculum = new Curriculum
            {
                LevelId       = set.LevelId,
                CnpnVersionId = request.ToCnpnVersionId,
                Reference     = set.Reference,
            };

            foreach (var stage in set.Stages)
            {
                var added = curriculum.AddStage(stage.StageId, stage.Coefficient, stage.DurationInDays);
                if (added.IsFailure) return Result.Failure<CnpnCloneResult>(added.Error);
                stages++;
            }

            dbContext.Curriculums.Add(curriculum);
            cloned++;
        }

        if (cloned > 0)
            await dbContext.SaveChangesAsync(ct);

        return new CnpnCloneResult(cloned, stages, skipped, outside);
    }
}
