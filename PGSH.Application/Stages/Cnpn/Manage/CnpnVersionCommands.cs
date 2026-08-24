using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Manage;

/// <summary>
/// Records a newly published ministerial text — the step that used to require SQL.
///
/// <para>A text carries three things that matter: what kind of degree it defines
/// (<paramref name="TotalYears"/> — six under 1650.25 where 2174.18 said seven), which intake it
/// begins to govern, and how it is cited. Its requirements per level are recorded afterwards, and
/// who it binds afterwards still.</para>
/// </summary>
public sealed record CreateCnpnVersionCommand(
    string          Code,
    string          Label,
    AcademicProgram AcademicProgram,
    int             TotalYears,
    string?         Reference,

    /// <summary>
    /// The first intake this text governs; new registrations from that year on are attached to it
    /// automatically. Null records a text kept for citation that governs nobody — as arrêté 2175.22
    /// became once 1650.25 disapplied it.
    /// </summary>
    int?            AppliesToEntrantsFromAcademicYearId)
    : ICommand<int>, IAuditableCommand
{
    public string  AuditAction     => "CNPN_VERSION_CREATED";
    public string  AuditEntityType => "CnpnVersion";
    public string? AuditEntityId   => Code;
    public string? AuditMetadata   => $$"""{"program":"{{AcademicProgram}}","totalYears":{{TotalYears}}}""";
}

/// <summary>
/// Corrects a recorded text. The programme is not editable: curricula and student stamps hang off
/// this row, and moving it to another filière would orphan every one of them.
/// </summary>
public sealed record UpdateCnpnVersionCommand(
    int     Id,
    string  Code,
    string  Label,
    int     TotalYears,
    string? Reference,
    int?    AppliesToEntrantsFromAcademicYearId)
    : ICommand, IAuditableCommand
{
    public string  AuditAction     => "CNPN_VERSION_UPDATED";
    public string  AuditEntityType => "CnpnVersion";
    public string? AuditEntityId   => Id.ToString();
    public string? AuditMetadata   => $$"""{"code":"{{Code}}","totalYears":{{TotalYears}}}""";
}

internal sealed class CreateCnpnVersionCommandValidator : AbstractValidator<CreateCnpnVersionCommand>
{
    public CreateCnpnVersionCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AcademicProgram).IsInEnum();
        RuleFor(x => x.TotalYears).InclusiveBetween(1, 10);
        RuleFor(x => x.Reference).MaximumLength(300);
    }
}

internal sealed class UpdateCnpnVersionCommandValidator : AbstractValidator<UpdateCnpnVersionCommand>
{
    public UpdateCnpnVersionCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TotalYears).InclusiveBetween(1, 10);
        RuleFor(x => x.Reference).MaximumLength(300);
    }
}

internal sealed class CreateCnpnVersionCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CreateCnpnVersionCommand, int>
{
    public async Task<Result<int>> Handle(CreateCnpnVersionCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<int>(access.Error);

        var guard = await CnpnVersionGuards.EnsureCodeAndIntakeAreFreeAsync(
            dbContext, request.AcademicProgram, request.Code,
            request.AppliesToEntrantsFromAcademicYearId, excludingId: null, ct);
        if (guard.IsFailure) return Result.Failure<int>(guard.Error);

        var version = new CnpnVersion
        {
            Code            = request.Code.Trim(),
            Label           = request.Label.Trim(),
            AcademicProgram = request.AcademicProgram,
            TotalYears      = request.TotalYears,
            Reference       = request.Reference?.Trim(),
            AppliesToEntrantsFromAcademicYearId = request.AppliesToEntrantsFromAcademicYearId,
        };

        dbContext.CnpnVersions.Add(version);
        await dbContext.SaveChangesAsync(ct);
        return version.Id;
    }
}

internal sealed class UpdateCnpnVersionCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<UpdateCnpnVersionCommand>
{
    public async Task<Result> Handle(UpdateCnpnVersionCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return access;

        var version = await dbContext.CnpnVersions
            .FirstOrDefaultAsync(v => v.Id == request.Id, ct);

        if (version is null)
            return Result.Failure(CnpnErrors.VersionNotFound(request.Id));

        var guard = await CnpnVersionGuards.EnsureCodeAndIntakeAreFreeAsync(
            dbContext, version.AcademicProgram, request.Code,
            request.AppliesToEntrantsFromAcademicYearId, excludingId: version.Id, ct);
        if (guard.IsFailure) return guard;

        // Shortening the degree must not strand a level that already carries requirements.
        int deepestRecorded = await dbContext.Curriculums
            .Where(c => c.CnpnVersionId == version.Id)
            .Select(c => (int?)c.Level.Year)
            .MaxAsync(ct) ?? 0;

        if (deepestRecorded > request.TotalYears)
            return Result.Failure(
                CnpnErrors.CannotShortenBelowRecordedLevel(request.TotalYears, deepestRecorded));

        // …nor a level the text has been declared to take effect for: the rule would point at a year
        // the programme no longer has.
        int deepestEffective = await dbContext.CnpnLevelEffectivities
            .Where(e => e.CnpnVersionId == version.Id)
            .Select(e => (int?)e.Level.Year)
            .MaxAsync(ct) ?? 0;

        if (deepestEffective > request.TotalYears)
            return Result.Failure(
                CnpnErrors.CannotShortenBelowEffectiveLevel(request.TotalYears, deepestEffective));

        version.Code       = request.Code.Trim();
        version.Label      = request.Label.Trim();
        version.TotalYears = request.TotalYears;
        version.Reference  = request.Reference?.Trim();
        version.AppliesToEntrantsFromAcademicYearId = request.AppliesToEntrantsFromAcademicYearId;

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal static class CnpnVersionGuards
{
    /// <summary>
    /// A code is unique within a programme, and two texts of one programme cannot claim the same
    /// first intake — version selection resolves "the latest intake at or before entry", and a tie
    /// has no defensible winner.
    /// </summary>
    public static async Task<Result> EnsureCodeAndIntakeAreFreeAsync(
        IApplicationDbContext dbContext,
        AcademicProgram program,
        string code,
        int? intakeYearId,
        int? excludingId,
        CancellationToken ct)
    {
        string trimmed = code.Trim();

        bool duplicateCode = await dbContext.CnpnVersions.AnyAsync(
            v => v.AcademicProgram == program
              && v.Code == trimmed
              && (excludingId == null || v.Id != excludingId), ct);

        if (duplicateCode)
            return Result.Failure(CnpnErrors.DuplicateCode(program, trimmed));

        if (intakeYearId is null)
            return Result.Success();

        if (!await dbContext.AcademicYears.AnyAsync(y => y.Id == intakeYearId, ct))
            return Result.Failure(StageErrors.AcademicYearNotFound(intakeYearId.Value));

        string? clash = await dbContext.CnpnVersions
            .Where(v => v.AcademicProgram == program
                     && v.AppliesToEntrantsFromAcademicYearId == intakeYearId
                     && (excludingId == null || v.Id != excludingId))
            .Select(v => v.Code)
            .FirstOrDefaultAsync(ct);

        return clash is null
            ? Result.Success()
            : Result.Failure(CnpnErrors.IntakeYearAlreadyTaken(program, clash));
    }
}
