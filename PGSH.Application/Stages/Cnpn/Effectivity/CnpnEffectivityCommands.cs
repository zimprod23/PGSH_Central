using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Effectivity;

/// <summary>
/// Records that a text governs a level from a year onward — « les stages de 3ᵉ année changent à
/// partir de 2025-2026 », « le texte à six ans s'applique à la 3ᵉ année et en dessous à partir de
/// 2026-2027 ».
///
/// <para>The second is three commands, one per level, and that is deliberate: the comparison
/// « ≤ 3ᵉ » is how a human describes the cut, not how it should be stored. Stored as a comparison it
/// would have to be re-evaluated to be read, and then a level added or renumbered later would
/// silently change which promotions a published text binds. Stored as rows it says exactly which
/// levels were meant, forever.</para>
/// </summary>
public sealed record CreateCnpnEffectivityCommand(
    int     CnpnVersionId,
    int     LevelId,
    int     FromAcademicYearId,
    string? Note) : ICommand<int>, IAuditableCommand
{
    public string  AuditAction     => "CNPN_EFFECTIVITY_CREATED";
    public string  AuditEntityType => "CnpnVersion";
    public string? AuditEntityId   => CnpnVersionId.ToString();
    public string? AuditMetadata   =>
        $$"""{"levelId":{{LevelId}},"fromAcademicYearId":{{FromAcademicYearId}}}""";
}

/// <summary>
/// Removes a rule. ⚠ <b>Prospective only.</b> Registrations already stamped under it keep their
/// text — that is the whole point of stamping them, and un-stamping them would move requirement sets
/// under students who have been studying against them. What the removal changes is which text the
/// <i>next</i> registration at that level resolves to.
/// </summary>
public sealed record DeleteCnpnEffectivityCommand(int Id) : ICommand<int>, IAuditableCommand
{
    public string  AuditAction     => "CNPN_EFFECTIVITY_DELETED";
    public string  AuditEntityType => "CnpnLevelEffectivity";
    public string? AuditEntityId   => Id.ToString();
    public string? AuditMetadata   => null;
}

internal sealed class CreateCnpnEffectivityCommandValidator
    : AbstractValidator<CreateCnpnEffectivityCommand>
{
    public CreateCnpnEffectivityCommandValidator()
    {
        RuleFor(x => x.CnpnVersionId).GreaterThan(0);
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.FromAcademicYearId).GreaterThan(0);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

internal sealed class DeleteCnpnEffectivityCommandValidator
    : AbstractValidator<DeleteCnpnEffectivityCommand>
{
    public DeleteCnpnEffectivityCommandValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

internal sealed class CreateCnpnEffectivityCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CreateCnpnEffectivityCommand, int>
{
    public async Task<Result<int>> Handle(CreateCnpnEffectivityCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<int>(access.Error);

        var version = await dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => v.Id == request.CnpnVersionId)
            .Select(v => new { v.Id, v.Code, v.AcademicProgram, v.TotalYears })
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return Result.Failure<int>(CnpnErrors.VersionNotFound(request.CnpnVersionId));

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Id, l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(ct);

        if (level is null)
            return Result.Failure<int>(LevelErrors.NotFound(request.LevelId));

        string levelLabel = level.Label ?? $"niveau {level.Id}";

        // « Retrait » is a withdrawal marker, not a year of study: no students to govern, no stages,
        // no cohorts. Same guard as the partition cut and auto-arrange.
        if (level.Year <= 0)
            return Result.Failure<int>(LevelErrors.NotAPromotion(levelLabel));

        if (level.AcademicProgram != version.AcademicProgram)
            return Result.Failure<int>(CnpnErrors.EffectivityProgramMismatch(
                version.Code, version.AcademicProgram, levelLabel, level.AcademicProgram));

        // A text that stops at six years cannot take effect for a seventh.
        if (level.Year > version.TotalYears)
            return Result.Failure<int>(
                CnpnErrors.CannotShortenBelowEffectiveLevel(version.TotalYears, level.Year));

        var year = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == request.FromAcademicYearId)
            .Select(y => new { y.Id, y.Label })
            .FirstOrDefaultAsync(ct);

        if (year is null)
            return Result.Failure<int>(StageErrors.AcademicYearNotFound(request.FromAcademicYearId));

        bool declared = await dbContext.CnpnLevelEffectivities.AnyAsync(
            e => e.CnpnVersionId == version.Id && e.LevelId == level.Id, ct);

        if (declared)
            return Result.Failure<int>(
                CnpnErrors.EffectivityAlreadyDeclared(version.Code, levelLabel, year.Label));

        string? clash = await dbContext.CnpnLevelEffectivities
            .Where(e => e.LevelId == level.Id && e.FromAcademicYearId == year.Id)
            .Select(e => e.CnpnVersion.Code)
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
            return Result.Failure<int>(
                CnpnErrors.EffectivityYearAlreadyTaken(levelLabel, year.Label, clash));

        var effectivity = new CnpnLevelEffectivity
        {
            CnpnVersionId      = version.Id,
            LevelId            = level.Id,
            FromAcademicYearId = year.Id,
            Note               = request.Note?.Trim(),
            RecordedOn         = DateTime.UtcNow,
        };

        dbContext.CnpnLevelEffectivities.Add(effectivity);
        await dbContext.SaveChangesAsync(ct);
        return effectivity.Id;
    }
}

internal sealed class DeleteCnpnEffectivityCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<DeleteCnpnEffectivityCommand, int>
{
    /// <summary>Returns how many registrations the rule had already stamped — they are left alone.</summary>
    public async Task<Result<int>> Handle(DeleteCnpnEffectivityCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<int>(access.Error);

        var effectivity = await dbContext.CnpnLevelEffectivities
            .Include(e => e.FromAcademicYear)
            .FirstOrDefaultAsync(e => e.Id == request.Id, ct);

        if (effectivity is null)
            return Result.Failure<int>(CnpnErrors.EffectivityNotFound(request.Id));

        int governed = await dbContext.Registrations.CountAsync(
            r => r.LevelId == effectivity.LevelId
              && r.CnpnVersionId == effectivity.CnpnVersionId
              && r.AcademicYear.StartDate >= effectivity.FromAcademicYear.StartDate, ct);

        dbContext.CnpnLevelEffectivities.Remove(effectivity);
        await dbContext.SaveChangesAsync(ct);

        return governed;
    }
}
