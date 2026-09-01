using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Curricula.Copy;

internal sealed class CopyCurriculumCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CopyCurriculumCommand, int>
{
    public async Task<Result<int>> Handle(CopyCurriculumCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure)
            return Result.Failure<int>(access.Error);

        var source = await dbContext.Curriculums
            .AsNoTracking()
            .Include(c => c.Stages)
            .FirstOrDefaultAsync(
                c => c.LevelId == request.LevelId && c.CnpnVersionId == request.FromCnpnVersionId,
                cancellationToken);

        if (source is null)
            return Result.Failure<int>(
                CurriculumErrors.NotFound(request.LevelId, request.FromCnpnVersionId));

        bool targetExists = await dbContext.Curriculums.AnyAsync(
            c => c.LevelId == request.LevelId && c.CnpnVersionId == request.ToCnpnVersionId,
            cancellationToken);

        // Copying is for opening a new text, not overwriting one. An existing set is amended through
        // Save, where each dropped stage is announced rather than silently replaced wholesale.
        if (targetExists)
            return Result.Failure<int>(
                CurriculumErrors.AlreadyExists(request.LevelId, request.ToCnpnVersionId));

        var targetVersion = await dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => v.Id == request.ToCnpnVersionId)
            .Select(v => new { v.TotalYears, v.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (targetVersion is null)
            return Result.Failure<int>(CurriculumErrors.VersionNotFound(request.ToCnpnVersionId));

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Year, l.AcademicProgram })
            .FirstAsync(cancellationToken);

        if (targetVersion.AcademicProgram != level.AcademicProgram)
            return Result.Failure<int>(CurriculumErrors.ProgramMismatch);

        // Copying a seven-year text onto a six-year one must not smuggle in a seventh year.
        if (level.Year > targetVersion.TotalYears)
            return Result.Failure<int>(
                CurriculumErrors.LevelOutsideProgramme(level.Year, targetVersion.TotalYears));

        var target = new Curriculum
        {
            LevelId       = request.LevelId,
            CnpnVersionId = request.ToCnpnVersionId,
            Reference     = source.Reference,
        };

        var result = target.CopyFrom(source);
        if (result.IsFailure)
            return Result.Failure<int>(result.Error);

        dbContext.Curriculums.Add(target);
        await dbContext.SaveChangesAsync(cancellationToken);

        return target.Id;
    }
}
