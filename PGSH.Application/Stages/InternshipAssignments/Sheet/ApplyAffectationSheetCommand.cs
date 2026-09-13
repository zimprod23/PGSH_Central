using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Delocalization;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet;

/// <summary>
/// Writes a promotion's affectations from an uploaded canevas: the cohortes it needs, the affectations
/// themselves, their périodes, and the délocalisations the file declares.
/// </summary>
/// <remarks>
/// <para><b>This is the most destructive act in the application, and it is meant to be used.</b> The
/// faculty plans in a spreadsheet; refusing to accept one back would not stop that, it would only stop
/// PGSH from knowing what was planned. So the bargain is not « make it hard », it is « make it
/// reversible where it can be, refuse where it cannot, and never let it act on anything the operator
/// has not been shown ».</para>
///
/// <para>⚠ <b>All or nothing, and that is the opposite of the mass délocalisation's bargain.</b> There,
/// one refused student leaves that student where he already was — a state somebody authored — so the
/// act writes what it can. Here the act <i>builds</i> a year's execution records: applying 800 lines
/// and refusing 12 leaves a promotion half-planned, and half-planned reads exactly like planned. One
/// error anywhere in the file therefore writes nothing, and the whole write is one transaction.</para>
///
/// <para>⚠ <b>Two counts, confirmed separately</b> — see <see cref="ConfirmedCount"/> and
/// <see cref="ConfirmedDroppedPeriods"/>. They move for different reasons, and it is the second that
/// nothing puts back.</para>
///
/// <para>⚠ <b>What it writes is hors grille.</b> The périodes carry no <c>CohortSlotAssignmentId</c>,
/// so the planning grid neither shows them nor counts them in the load it prints, and « dépublier »
/// will not take them back — it is the inverse of publishing and these came from somewhere else. The
/// report says so in words, because a grid that looks empty for a promotion that is in fact planned is
/// precisely the one number standing for two states this codebase keeps shipping.</para>
///
/// <para>⚠ <b>Expect the write to take a while on a whole promotion.</b> Each affectation raises
/// <c>AffectationImportedDomainEvent</c> and <c>ApplicationDbContext</c> publishes events after the
/// commit, one at a time, each writing a <c>History</c> row. The transaction itself is quick; the
/// dossier entries appearing afterwards are progress, not a hang — and past
/// <c>AffectationSheetPlanner.LargeActThreshold</c> the aperçu says so in words, because an act that
/// has already succeeded and merely looks slow is exactly the one somebody interrupts.</para>
///
/// <para>⚠ <b>The read half, by contrast, is measured and fine.</b> On the live base 13/09/2026 the
/// largest canvas this faculty can produce — 6ᵉ MED, 701 students × 6 stages = <b>4 206 lines</b> —
/// downloaded in 685 ms and previewed in <b>545 ms</b>. The queries are flat and array-parameterised
/// (<c>= ANY(@p)</c>, so no parameter ceiling to hit) and the per-line work is dictionary lookups. It
/// is the write that scales with the promotion, and only through the events.</para>
/// </remarks>
/// <param name="ConfirmedCount">
/// The number of <b>affectations</b> the aperçu said would be written — not lines. A rotation is
/// several lines, and the number on screen has to be the number being authorised.
/// </param>
/// <param name="ConfirmedDroppedPeriods">
/// The number of existing périodes the aperçu said would be destroyed. ⚠ Confirmed apart from the
/// creations: a période evaluated between the aperçu and the apply changes what is destroyed without
/// changing what is written, and destruction is the half that is final.
/// </param>
public sealed record ApplyAffectationSheetCommand(
    IReadOnlyList<AffectationSheetRow> Rows,
    int LevelId,
    int ConfirmedCount,
    int ConfirmedDroppedPeriods,
    int? AcademicYearId = null) : ICommand<AffectationSheetReport>, IAuditableCommand
{
    public string  AuditAction     => "AFFECTATION_SHEET_APPLIED";
    public string  AuditEntityType => "Level";
    public string? AuditEntityId   => LevelId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        academicYearId          = AcademicYearId,
        rows                    = Rows.Count,
        confirmedCount          = ConfirmedCount,
        confirmedDroppedPeriods = ConfirmedDroppedPeriods,
    });
}

internal sealed class ApplyAffectationSheetCommandValidator
    : AbstractValidator<ApplyAffectationSheetCommand>
{
    public ApplyAffectationSheetCommandValidator()
    {
        RuleFor(x => x.Rows).NotNull();
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.ConfirmedCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ConfirmedDroppedPeriods).GreaterThanOrEqualTo(0);
    }
}

internal sealed class ApplyAffectationSheetCommandHandler(
    IApplicationDbContext dbContext,
    AffectationSheetPlanner planner,
    ExecutionAuthorizer authorizer,
    IAuditTrail auditTrail)
    : ICommandHandler<ApplyAffectationSheetCommand, AffectationSheetReport>
{
    public async Task<Result<AffectationSheetReport>> Handle(
        ApplyAffectationSheetCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AffectationSheetErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<AffectationSheetReport>(access.Error);

        // ⚠ Through the trail, never through the context's own helper: a retry clears the change
        // tracker, and only the trail knows which version of the pending journal entry is current.
        return await auditTrail.RunAtomicallyAsync(
            ct => WriteAsync(request, ct), cancellationToken);
    }

    private async Task<Result<AffectationSheetReport>> WriteAsync(
        ApplyAffectationSheetCommand request, CancellationToken ct)
    {
        var planned = await planner.PlanAsync(request.LevelId, request.AcademicYearId, request.Rows, ct);
        if (planned.IsFailure)
            return Result.Failure<AffectationSheetReport>(planned.Error);

        var plan = planned.Value;

        if (plan.Report.ErrorCount > 0)
            return Result.Failure<AffectationSheetReport>(
                AffectationSheetErrors.HasErrors(plan.Report.ErrorCount));

        // ⚠ Against the plan's own collections rather than the report's numbers — they are the same
        // figures, and asking what is about to be executed is the one that cannot drift from what runs.
        if (request.ConfirmedCount != plan.Work.Count)
            return Result.Failure<AffectationSheetReport>(
                AffectationSheetErrors.CountMismatch(request.ConfirmedCount, plan.Work.Count));

        int toDrop = plan.Work.Sum(w => w.PeriodsDropped);
        if (request.ConfirmedDroppedPeriods != toDrop)
            return Result.Failure<AffectationSheetReport>(
                AffectationSheetErrors.DroppedMismatch(request.ConfirmedDroppedPeriods, toDrop));

        var cohorts = await EnsureCohortsAsync(plan, ct);
        var assignments = await LoadTrackedAssignmentsAsync(plan, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int created = 0, rebuilt = 0, delocalized = 0, periodsWritten = 0, periodsDropped = 0;

        foreach (var item in plan.Work)
        {
            int cohortId = cohorts[(item.AcademicGroupId, item.StageId)];

            var assignment = item.ExistingAssignmentId is { } id ? assignments[id] : null;
            bool isNew = assignment is null;

            assignment ??= DelocalizationAssignmentFactory.CreateFor(
                item.RegistrationId, cohortId, today);

            if (item.IsDelocalization)
            {
                var period = item.Periods[0];

                // Through the aggregate, which drops whatever was there and records the motif. The
                // planner already refused every case Delocalize refuses, so a failure here is the two
                // disagreeing — worth failing the whole act over rather than writing half a file.
                var result = assignment.Delocalize(
                    item.StageId, period.ServiceId, period.StartDate, period.EndDate,
                    period.Reason!, demandeId: null);

                if (result.IsFailure)
                    return Result.Failure<AffectationSheetReport>(result.Error);

                delocalized++;
                periodsWritten++;
            }
            else
            {
                var result = assignment.DeclareRotation(
                    item.StageId,
                    [.. item.Periods.Select(p => new DeclaredPeriod(p.ServiceId, p.StartDate, p.EndDate))]);

                if (result.IsFailure)
                    return Result.Failure<AffectationSheetReport>(result.Error);

                periodsDropped += result.Value;
                periodsWritten += item.Periods.Count;
                if (isNew) created++; else rebuilt++;
            }

            if (isNew)
                dbContext.InternshipAssignments.Add(assignment);
        }

        // ⚠ Before the save, and it is the act's own statement of what it did. « Canevas appliqué » on
        // a virgin promotion and on a published one are the same code and unrelated events; the code
        // alone cannot tell the register which one happened.
        auditTrail.RecordOutcome(
            ("affectationsCreated", created),
            ("affectationsRebuilt", rebuilt),
            ("delocalizations", delocalized),
            ("periodsWritten", periodsWritten),
            ("periodsDropped", periodsDropped),
            ("cohortsCreated", plan.CohortsToCreate.Count),
            ("unchanged", plan.Report.Unchanged));

        await dbContext.SaveChangesAsync(ct);
        return plan.Report;
    }

    /// <summary>
    /// The cohorte of every (roster, stage) the file needs, creating the missing ones.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Saved before the affectations, inside the same transaction.</b> A <c>Cohort</c> has a
    /// store-generated <c>int</c> key and the affectations need it; letting EF fix it up through the
    /// navigation would work for the assignment itself but not for the <c>CohortMembership</c> the
    /// factory builds, which carries the id by value. One extra round-trip, no half-state: the
    /// enclosing transaction covers both.
    /// </remarks>
    private async Task<Dictionary<(int, int), int>> EnsureCohortsAsync(
        AffectationSheetPlan plan, CancellationToken ct)
    {
        var groupIds = plan.Work.Select(w => w.AcademicGroupId).Distinct().ToList();
        var stageIds = plan.Work.Select(w => w.StageId).Distinct().ToList();

        var existing = await AffectationSheetPlanner.CohortsQuery(dbContext, groupIds, stageIds)
            .AsNoTracking()
            .ToListAsync(ct);

        var byKey = existing.ToDictionary(c => (c.AcademicGroupId, c.StageId), c => c.CohortId);

        // A cohorte carries its roster's label, exactly as CohortProvisioner writes it. A blank one
        // here would make the cohortes this act creates recognisable in every list as the ones a
        // spreadsheet made — a distinction nobody asked for and nobody can act on.
        var labels = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Label, ct);

        var fresh = plan.CohortsToCreate
            .Where(key => !byKey.ContainsKey(key))
            .Select(key => new Cohort
            {
                AcademicGroupId = key.AcademicGroupId,
                StageId         = key.StageId,
                Label           = labels.TryGetValue(key.AcademicGroupId, out string? label)
                                    ? label
                                    : string.Empty,
            })
            .ToList();

        if (fresh.Count == 0)
            return byKey;

        dbContext.Cohorts.AddRange(fresh);
        await dbContext.SaveChangesAsync(ct);

        foreach (var cohort in fresh)
            byKey[(cohort.AcademicGroupId, cohort.StageId)] = cohort.Id;

        return byKey;
    }

    /// <summary>
    /// The affectations the file rebuilds, tracked, with their périodes and évaluations loaded.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The <c>Include</c> of the évaluation is load-bearing, not defensive.</b>
    /// <c>DeclareRotation</c> and <c>Delocalize</c> both refuse over a mark by reading
    /// <c>ServicePeriod.Evaluation</c>, and an un-Included navigation is indistinguishable from an
    /// absent one — the guard would answer « rien à perdre » on a stage that carries a note. ⚠ The
    /// in-memory provider fixes navigations up from the change tracker, so this suite cannot see the
    /// mistake either.
    /// </remarks>
    private async Task<Dictionary<Guid, InternshipAssignment>> LoadTrackedAssignmentsAsync(
        AffectationSheetPlan plan, CancellationToken ct)
    {
        var ids = plan.Work
            .Where(w => w.ExistingAssignmentId is not null)
            .Select(w => w.ExistingAssignmentId!.Value)
            .ToList();

        if (ids.Count == 0)
            return [];

        return await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation)
            .Include(a => a.MembershipHistory)
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
    }
}
