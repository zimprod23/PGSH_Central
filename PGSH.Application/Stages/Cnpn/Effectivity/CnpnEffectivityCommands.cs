using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
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

        // Tracked, with the rules it already carries: the text decides for itself whether it may
        // speak for this level, and « il régit déjà ce niveau » is a question about its own children.
        var version = await dbContext.CnpnVersions
            .Include(v => v.LevelEffectivities)
            .FirstOrDefaultAsync(v => v.Id == request.CnpnVersionId, ct);

        if (version is null)
            return Result.Failure<int>(CnpnErrors.VersionNotFound(request.CnpnVersionId));

        var level = await dbContext.Levels.FirstOrDefaultAsync(l => l.Id == request.LevelId, ct);

        if (level is null)
            return Result.Failure<int>(LevelErrors.NotFound(request.LevelId));

        var year = await dbContext.AcademicYears
            .FirstOrDefaultAsync(y => y.Id == request.FromAcademicYearId, ct);

        if (year is null)
            return Result.Failure<int>(StageErrors.AcademicYearNotFound(request.FromAcademicYearId));

        // ⚠ The one rule the aggregate cannot decide: it is about the *other* texts. Two of them
        // starting to govern one level in one year has no defensible winner, since resolution takes
        // the latest start date at or before the registration's year.
        string? clash = await dbContext.CnpnLevelEffectivities
            .Where(e => e.LevelId == level.Id && e.FromAcademicYearId == year.Id)
            .Select(e => e.CnpnVersion.Code)
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
            return Result.Failure<int>(CnpnErrors.EffectivityYearAlreadyTaken(
                level.Label ?? $"niveau {level.Id}", year.Label, clash));

        var declared = version.DeclareEffectivity(level, year, request.Note, DateTime.UtcNow);
        if (declared.IsFailure) return Result.Failure<int>(declared.Error);

        await dbContext.SaveChangesAsync(ct);
        return declared.Value.Id;
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

        var rule = await dbContext.CnpnLevelEffectivities
            .AsNoTracking()
            .Where(e => e.Id == request.Id)
            .Select(e => new { e.Id, e.CnpnVersionId, e.LevelId, From = e.FromAcademicYear.StartDate })
            .FirstOrDefaultAsync(ct);

        if (rule is null)
            return Result.Failure<int>(CnpnErrors.EffectivityNotFound(request.Id));

        // Counted before the write, like every other act here that reports what it leaves behind:
        // afterwards there is no rule to count against.
        int governed = await dbContext.Registrations.CountAsync(
            r => r.LevelId == rule.LevelId
              && r.CnpnVersionId == rule.CnpnVersionId
              && r.AcademicYear.StartDate >= rule.From, ct);

        var version = await dbContext.CnpnVersions
            .Include(v => v.LevelEffectivities)
            .FirstOrDefaultAsync(v => v.Id == rule.CnpnVersionId, ct);

        if (version is null)
            return Result.Failure<int>(CnpnErrors.VersionNotFound(rule.CnpnVersionId));

        var withdrawn = version.WithdrawEffectivity(rule.Id);
        if (withdrawn.IsFailure) return Result.Failure<int>(withdrawn.Error);

        dbContext.CnpnLevelEffectivities.Remove(withdrawn.Value);

        await dbContext.SaveChangesAsync(ct);

        return governed;
    }
}
