using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;

/// <summary>
/// What undoing this import would do. Writes nothing; the report <em>is</em> the plan the act
/// executes, because both run <see cref="AffectationImportReversalPlanner"/> and nothing else.
/// </summary>
public sealed record PreviewAffectationImportReversalQuery(Guid ImportId)
    : IQuery<AffectationImportReversalReport>;

internal sealed class PreviewAffectationImportReversalQueryHandler(
    AffectationImportReversalPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewAffectationImportReversalQuery, AffectationImportReversalReport>
{
    public async Task<Result<AffectationImportReversalReport>> Handle(
        PreviewAffectationImportReversalQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AffectationSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<AffectationImportReversalReport>(access.Error);

        var plan = await planner.PlanAsync(request.ImportId, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<AffectationImportReversalReport>(plan.Error)
            : plan.Value.Report;
    }
}

/// <summary>
/// Walks one application of the canevas back: removes the affectations it created, and writes back the
/// rotations it replaced, exactly as they stood.
/// </summary>
/// <remarks>
/// <para><b>Why this can exist at all.</b> The import refuses to destroy a mark
/// (<c>AlreadyMarked</c>) or a day of attendance (<c>AlreadyAttended</c>), so everything it does
/// destroy is a service, a window, a few flags and — where it applies — a grid cell and a
/// délocalisation motif. All of that is recorded in <c>AffectationImport</c>. The undo is therefore
/// <b>total</b>, not approximate, and that property is worth more than the feature: an undo that
/// silently restores less than it removed is worse than no undo at all, because somebody trusts it.</para>
///
/// <para>⚠ <b>All or nothing, like the import.</b> One affectation changed since — evaluated,
/// attended, re-planned — refuses the whole reversal. Undoing 800 and refusing 12 would leave a
/// promotion in a state neither the import nor the undo describes, and nobody could say which.</para>
///
/// <para>⚠ <b><see cref="ConfirmedCount"/> confirms the destructive half</b>: affectations the import
/// created, which the undo deletes whole. Restoring a rotation is reversible — the file can be sent
/// again — but nothing puts a deleted affectation back except another import, so that is the number
/// the operator is shown and sends back.</para>
///
/// <para>⚠ <b>The import record survives the reversal.</b> « Cet étudiant a-t-il été planifié par un
/// fichier, puis dé-planifié ? » is a question the dossier must answer, and a row that deletes itself
/// on undo answers « il ne s'est rien passé ».</para>
/// </remarks>
public sealed record ReverseAffectationImportCommand(Guid ImportId, int ConfirmedCount)
    : ICommand<AffectationImportReversalReport>, IAuditableCommand
{
    public string  AuditAction     => "AFFECTATION_IMPORT_REVERSED";
    public string  AuditEntityType => "AffectationImport";
    public string? AuditEntityId   => ImportId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new { confirmedCount = ConfirmedCount });
}

internal sealed class ReverseAffectationImportCommandValidator
    : AbstractValidator<ReverseAffectationImportCommand>
{
    public ReverseAffectationImportCommandValidator()
    {
        RuleFor(x => x.ImportId).NotEmpty();
        RuleFor(x => x.ConfirmedCount).GreaterThanOrEqualTo(0);
    }
}

internal sealed class ReverseAffectationImportCommandHandler(
    IApplicationDbContext dbContext,
    AffectationImportReversalPlanner planner,
    ExecutionAuthorizer authorizer,
    IAuditTrail auditTrail)
    : ICommandHandler<ReverseAffectationImportCommand, AffectationImportReversalReport>
{
    public async Task<Result<AffectationImportReversalReport>> Handle(
        ReverseAffectationImportCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AffectationSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<AffectationImportReversalReport>(access.Error);

        return await auditTrail.RunAtomicallyAsync(ct => ReverseAsync(request, ct), cancellationToken);
    }

    private async Task<Result<AffectationImportReversalReport>> ReverseAsync(
        ReverseAffectationImportCommand request, CancellationToken ct)
    {
        var planned = await planner.PlanAsync(request.ImportId, ct);
        if (planned.IsFailure)
            return Result.Failure<AffectationImportReversalReport>(planned.Error);

        var plan = planned.Value;

        if (plan.Report.ErrorCount > 0)
            return Result.Failure<AffectationImportReversalReport>(
                AffectationImportReversalErrors.HasChanged(plan.Report.ErrorCount));

        int toRemove = plan.Work.Count(w => w.Outcome == AffectationImportOutcome.Created);
        if (request.ConfirmedCount != toRemove)
            return Result.Failure<AffectationImportReversalReport>(
                AffectationImportReversalErrors.CountMismatch(request.ConfirmedCount, toRemove));

        var import = await dbContext.AffectationImports
            .FirstOrDefaultAsync(i => i.Id == request.ImportId, ct);

        if (import is null)
            return Result.Failure<AffectationImportReversalReport>(
                StageErrors.AffectationImportNotFound(request.ImportId));

        var affectations = await LoadTrackedAsync(plan, ct);

        int removed = 0, restored = 0, periodsRestored = 0;

        foreach (var item in plan.Work)
        {
            if (!affectations.TryGetValue(item.InternshipAssignmentId, out var affectation))
                continue;

            if (item.Outcome == AffectationImportOutcome.Created)
            {
                // The import made it; there was nothing before it. Its périodes, membership and
                // dossier entries go with it by cascade.
                dbContext.InternshipAssignments.Remove(affectation);
                removed++;
                continue;
            }

            var result = affectation.RestoreRotation(item.StageId, item.Restore);
            if (result.IsFailure)
                return Result.Failure<AffectationImportReversalReport>(result.Error);

            restored++;
            periodsRestored += item.Restore.Count;
        }

        var marked = import.Reverse(DateTime.UtcNow, await authorizer.CurrentUserIdAsync(ct));
        if (marked.IsFailure)
            return Result.Failure<AffectationImportReversalReport>(marked.Error);

        auditTrail.RecordOutcome(
            ("affectationsRemoved", removed),
            ("rotationsRestored", restored),
            ("periodsRestored", periodsRestored),
            ("alreadyGone", plan.Report.AlreadyGone));

        await dbContext.SaveChangesAsync(ct);
        return plan.Report;
    }

    /// <summary>
    /// The affectations the undo touches, tracked, with everything the aggregate's guards read.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>ServicePeriods</c>, their <c>Evaluation</c> <b>and</b> their <c>Attendance</c>:
    /// <c>RestoreRotation</c> refuses over either, and an un-<c>Include</c>d navigation is
    /// indistinguishable from an absent one — the guard would answer « rien à perdre » on an
    /// affectation that has since been marked or attended. The in-memory provider fixes navigations up
    /// from the change tracker, so this suite cannot see the mistake either.
    /// </remarks>
    private async Task<Dictionary<Guid, InternshipAssignment>> LoadTrackedAsync(
        AffectationImportReversalPlan plan, CancellationToken ct)
    {
        var ids = plan.Work.Select(w => w.InternshipAssignmentId).ToList();
        if (ids.Count == 0) return [];

        return await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Attendance)
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
    }
}
