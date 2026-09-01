using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.AcademicYears.Manage;

/// <summary>
/// Removes an academic year. Meant for the year created by mistake — a typo in the label, a span
/// entered for the wrong promotion, a year opened a cycle too early.
/// </summary>
/// <remarks>
/// <para>⚠ <b>An ungated delete here is destructive in two different ways, and neither announces
/// itself.</b> Measured against the schema on 2026-08-24:</para>
///
/// <list type="bullet">
///   <item><c>Registrations</c>, <c>StageSlots</c>, <c>FinalYearEntryWaivers</c>,
///   <c>CnpnLevelEffectivities.FromAcademicYearId</c> and
///   <c>CnpnVersions.AppliesToEntrantsFromAcademicYearId</c> are all <c>RESTRICT</c> — so the delete
///   comes back as a raw foreign-key violation, i.e. a 500 with nothing in it a user can act on.</item>
///   <item><c>AcademicGroups.AcademicYearId</c> is <b>CASCADE</b> — so the rosters of that year go
///   with it, silently. The chain stops there only because <c>Cohorts</c> and <c>Registrations</c>
///   restrict on the roster; a year whose rosters carry cohorts fails with the same opaque 500.</item>
/// </list>
///
/// <para>So the command refuses while anything year-constituted exists and <b>names every count</b>,
/// and where only empty rosters remain it reports how many the cascade will take rather than removing
/// them quietly. That is the same bargain <c>DeleteCnpnVersionCommand</c> strikes, and it rests on the
/// same justification: what is allowed to cascade is exactly what has no meaning once the thing that
/// constituted it is gone. An empty roster of a year nobody registered in records nothing.</para>
///
/// <para>Deleting the <b>current</b> year is refused outright: every handler that omits a year
/// resolves through it, so the application would be left with no answer to « quelle année ? ».
/// Designating another year first is reversible; this is not.</para>
/// </remarks>
public sealed record DeleteAcademicYearCommand(int AcademicYearId)
    : ICommand<DeletedAcademicYearReport>, IAuditableCommand
{
    public string AuditAction => "ACADEMIC_YEAR_DELETED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();
    public string? AuditMetadata => JsonSerializer.Serialize(new { academicYearId = AcademicYearId });
}

/// <param name="RostersRemoved">
/// Empty rosters the cascade took. Reported rather than hidden — it is the only thing the delete
/// destroyed, and the only number that cannot be read back afterwards.
/// </param>
public sealed record DeletedAcademicYearReport(string Label, int RostersRemoved);

internal sealed class DeleteAcademicYearCommandValidator : AbstractValidator<DeleteAcademicYearCommand>
{
    public DeleteAcademicYearCommandValidator() =>
        RuleFor(x => x.AcademicYearId).GreaterThan(0);
}

internal sealed class DeleteAcademicYearCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<DeleteAcademicYearCommand, DeletedAcademicYearReport>
{
    public async Task<Result<DeletedAcademicYearReport>> Handle(
        DeleteAcademicYearCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AcademicYearErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<DeletedAcademicYearReport>(access.Error);

        var year = await dbContext.AcademicYears
            .Include(y => y.Groups)
            .FirstOrDefaultAsync(y => y.Id == request.AcademicYearId, cancellationToken);

        if (year is null)
            return Result.Failure<DeletedAcademicYearReport>(
                AcademicYearErrors.NotFound(request.AcademicYearId));

        if (year.IsCurrent)
            return Result.Failure<DeletedAcademicYearReport>(
                AcademicYearErrors.CannotDeleteCurrent(year.Label));

        var holdings = await DescribeHoldingsAsync(year.Id, cancellationToken);
        if (holdings.Count > 0)
            return Result.Failure<DeletedAcademicYearReport>(
                AcademicYearErrors.StillInUse(year.Label, holdings));

        int rostersRemoved = year.Groups.Count;

        // ⚠ No domain event here, deliberately. ApplicationDbContext collects events from
        // ChangeTracker.Entries<Entity>() *after* base.SaveChangesAsync, and EF detaches Deleted
        // entries on AcceptAllChanges — so an event raised on a row being removed is silently
        // dropped, never published. The deletion is on the record through IAuditableCommand, which
        // does not depend on the entity surviving the save.
        dbContext.AcademicYears.Remove(year);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeletedAcademicYearReport(year.Label, rostersRemoved);
    }

    /// <summary>
    /// Every reason the year cannot go, in the words the refusal will use. Counted rather than
    /// short-circuited on the first hit: a user who clears the registrations only to be told about the
    /// slots has been sent round the loop twice for no reason.
    /// </summary>
    private async Task<List<string>> DescribeHoldingsAsync(int yearId, CancellationToken ct)
    {
        var holdings = new List<string>();

        int registrations = await dbContext.Registrations
            .CountAsync(r => r.AcademicYearId == yearId, ct);
        if (registrations > 0) holdings.Add($"{registrations} inscription(s)");

        int slots = await dbContext.StageSlots
            .CountAsync(s => s.AcademicYearId == yearId, ct);
        if (slots > 0) holdings.Add($"{slots} période(s) de stage");

        // A roster carrying cohorts is not an empty shell, and the cascade cannot take it: Cohorts
        // restricts on AcademicGroups, so the delete would surface as a 500 instead of a refusal.
        int cohorts = await dbContext.Cohorts
            .CountAsync(c => c.AcademicGroup.AcademicYearId == yearId, ct);
        if (cohorts > 0) holdings.Add($"{cohorts} cohorte(s)");

        int waivers = await dbContext.FinalYearEntryWaivers
            .CountAsync(w => w.AcademicYearId == yearId, ct);
        if (waivers > 0) holdings.Add($"{waivers} dérogation(s) de dernière année");

        int effectivities = await dbContext.CnpnLevelEffectivities
            .CountAsync(e => e.FromAcademicYearId == yearId, ct);
        if (effectivities > 0) holdings.Add($"{effectivities} règle(s) d'entrée en vigueur CNPN");

        int intakes = await dbContext.CnpnVersions
            .CountAsync(v => v.AppliesToEntrantsFromAcademicYearId == yearId, ct);
        if (intakes > 0) holdings.Add($"{intakes} CNPN dont c'est l'année d'entrée");

        return holdings;
    }
}
