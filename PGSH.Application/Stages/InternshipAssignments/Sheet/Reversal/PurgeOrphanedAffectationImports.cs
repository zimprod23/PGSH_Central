using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Audit;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;

/// <summary>
/// Imports whose subjects are all gone — every registration they name has since been deleted.
/// </summary>
/// <remarks>
/// <para><b>An import survives its reversal, but not its subjects.</b> <c>AffectationImport</c> is kept
/// after an undo on purpose: « cet étudiant a-t-il été planifié par un fichier, puis dé-planifié ? » is
/// a question the dossier must answer, and a row that erases itself answers « il ne s'est rien passé ».
/// ⚠ That reasoning needs a student to ask it about. When every registration an import names has been
/// deleted, the question has no subject and the row documents nothing — it is litter, and it will
/// accumulate on its own every time scolarité removes a student.</para>
///
/// <para>⚠ <b>All, never any.</b> An import that still names one surviving registration is kept whole:
/// it documents a real act on a real student, and deleting it to tidy the others would lose that. The
/// predicate is deliberately the strictest one that makes the row meaningless.</para>
///
/// <para>⚠ <b>And this is the one deletion the register does not keep</b>, which is why it goes through
/// an act with a confirmed count and an audit entry rather than through SQL. A row removed by hand
/// leaves nothing behind saying it existed; this leaves the <c>AuditLog</c> entry and its count.</para>
/// </remarks>
public sealed record GetOrphanedAffectationImportsQuery(int? LevelId = null, int? AcademicYearId = null)
    : IQuery<IReadOnlyList<AffectationImportSummary>>;

internal sealed class GetOrphanedAffectationImportsQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetOrphanedAffectationImportsQuery, IReadOnlyList<AffectationImportSummary>>
{
    public async Task<Result<IReadOnlyList<AffectationImportSummary>>> Handle(
        GetOrphanedAffectationImportsQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AffectationSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<IReadOnlyList<AffectationImportSummary>>(access.Error);

        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<IReadOnlyList<AffectationImportSummary>>(year.Error);

        var orphaned = await OrphanedQuery(dbContext, year.Value, request.LevelId)
            .AsNoTracking()
            .Select(i => new AffectationImportSummary(
                i.Id,
                i.AcademicYear.Label,
                dbContext.Levels.Where(l => l.Id == i.LevelId).Select(l => l.Label).FirstOrDefault()
                    ?? $"Niveau {i.LevelId}",
                i.FileName,
                i.AppliedAtUtc,
                dbContext.Users.Where(u => u.Id == i.AppliedByUserId)
                    .Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault(),
                i.Status,
                i.ReversedAtUtc,
                i.AffectationCount,
                i.ReplacedPeriodCount,
                // ⚠ Never reversible: whatever it wrote went with the students it named.
                false))
            .ToListAsync(cancellationToken);

        return orphaned;
    }

    /// <summary>
    /// ⚠ <b>The predicate is « no entry names a surviving registration »</b>, expressed as a negated
    /// <c>Any</c> so it is one <c>NOT EXISTS</c> the provider can translate — never a collection folded
    /// into a projection. An import with no entries at all is orphaned by the same reading: it names
    /// nobody.
    /// </summary>
    internal static IQueryable<AffectationImport> OrphanedQuery(
        IApplicationDbContext dbContext, int academicYearId, int? levelId) =>
        dbContext.AffectationImports
            .Where(i => i.AcademicYearId == academicYearId
                     && (levelId == null || i.LevelId == levelId))
            .Where(i => !i.Entries.Any(e =>
                dbContext.Registrations.Any(r => r.Id == e.RegistrationId)));
}

/// <summary>
/// Removes the imports whose subjects are all gone. See <see cref="GetOrphanedAffectationImportsQuery"/>
/// for why that is the one case where a kept record stops being worth keeping.
/// </summary>
/// <param name="ConfirmedCount">
/// The number the operator was shown. ⚠ Same guard as every other bulk act here: this deletes rows
/// nobody named one by one, and a student deleted between the list and the purge changes what goes
/// without changing anything on screen.
/// </param>
public sealed record PurgeOrphanedAffectationImportsCommand(
    int ConfirmedCount,
    int? LevelId = null,
    int? AcademicYearId = null) : ICommand<int>, IAuditableCommand
{
    public string  AuditAction     => "AFFECTATION_IMPORTS_PURGED";
    public string  AuditEntityType => "Level";
    public string? AuditEntityId   => LevelId?.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        academicYearId = AcademicYearId,
        confirmedCount = ConfirmedCount,
    });
}

internal sealed class PurgeOrphanedAffectationImportsCommandValidator
    : AbstractValidator<PurgeOrphanedAffectationImportsCommand>
{
    public PurgeOrphanedAffectationImportsCommandValidator() =>
        RuleFor(x => x.ConfirmedCount).GreaterThanOrEqualTo(0);
}

internal sealed class PurgeOrphanedAffectationImportsCommandHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ExecutionAuthorizer authorizer,
    IAuditTrail auditTrail)
    : ICommandHandler<PurgeOrphanedAffectationImportsCommand, int>
{
    public async Task<Result<int>> Handle(
        PurgeOrphanedAffectationImportsCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AffectationSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<int>(access.Error);

        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<int>(year.Error);

        return await auditTrail.RunAtomicallyAsync(async ct =>
        {
            // Tracked, so the entries and their replaced périodes go by cascade rather than by a
            // second statement — and so this reaches a SaveChanges, which is what commits the
            // journal entry the pipeline staged.
            var orphaned = await GetOrphanedAffectationImportsQueryHandler
                .OrphanedQuery(dbContext, year.Value, request.LevelId)
                .ToListAsync(ct);

            if (request.ConfirmedCount != orphaned.Count)
                return Result.Failure<int>(
                    AffectationImportReversalErrors.PurgeCountMismatch(
                        request.ConfirmedCount, orphaned.Count));

            dbContext.AffectationImports.RemoveRange(orphaned);

            auditTrail.RecordOutcome(
                ("importsPurged", orphaned.Count),
                ("affectationsDocumented", orphaned.Sum(i => i.AffectationCount)),
                ("files", string.Join(", ", orphaned.Select(i => i.FileName).Where(f => f is not null))));

            await dbContext.SaveChangesAsync(ct);
            return Result.Success(orphaned.Count);
        }, cancellationToken);
    }
}
