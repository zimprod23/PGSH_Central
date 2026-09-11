using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Common.Utils;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Students.Selection;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.BulkAssignment;

/// <summary>
/// Works out what putting a named list of students into one roster would do — and is the <b>only</b>
/// code that does, for the preview and for the apply alike.
/// </summary>
/// <remarks>
/// <para><b>The act this serves.</b> A form circulates, a list of volunteers comes back, and those
/// students are to make up the roster that goes to a partner hospital. Today that is one dialog per
/// student; on a hundred volunteers it is a hundred dialogs, which is how a real list stops being
/// used at all.</para>
///
/// <para>⚠ <b>Two verbs, decided per student, never one.</b> A registration in no roster is
/// <i>attached</i> (affectations created from the target's cohortes); one already in a roster is
/// <i>moved</i> (affectations re-pointed, membership rewritten in place, no trace). Choosing between
/// them at apply time by re-reading the row would let the answer change between what the operator
/// confirmed and what runs.</para>
///
/// <para>⚠ <b>The guards are the single acts' own guards, read the same way.</b> The engagement
/// check is <c>AffectationToll.IsUnderway</c>, narrowed to the source roster exactly as
/// <c>StudentGroupRelocator</c> narrows it — a revalidation placed by hand into another cohorte is
/// not the roster's doing and has no business refusing this. A planner that decided « underway » on
/// its own terms would be a second rule with nothing to catch it disagreeing with the first.</para>
/// </remarks>
internal sealed class BulkRosterAssignmentPlanner(
    IApplicationDbContext dbContext,
    StudentSelectionResolver selectionResolver,
    AcademicYearResolver yearResolver)
{
    public async Task<Result<BulkRosterAssignmentPlan>> PlanAsync(
        int targetGroupId,
        int? academicYearId,
        StudentTargets targets,
        CancellationToken ct)
    {
        var year = await yearResolver.ResolveWithLabelAsync(academicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<BulkRosterAssignmentPlan>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        var target = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == targetGroupId)
            .Select(g => new { g.Id, g.Label, g.AcademicYearId, g.LevelId })
            .FirstOrDefaultAsync(ct);

        if (target is null)
            return Result.Failure<BulkRosterAssignmentPlan>(
                AcademicGroupErrors.NotFound(targetGroupId));

        string targetLabel = target.Label ?? $"Groupe {targetGroupId}";

        // ⚠ The roster's own year decides, not the one the caller asked for. A roster is keyed
        // (année, niveau, numéro), so a target from another year would have every downstream check
        // keyed on a roster the registrations cannot belong to — and the act would report refusals
        // naming the students rather than the mistake, which is on the selection above them.
        if (target.AcademicYearId != yearId)
            return Result.Failure<BulkRosterAssignmentPlan>(
                BulkRosterAssignmentErrors.TargetInAnotherYear(targetLabel, yearLabel));

        // ⚠ « Non réparti » is never a destination here: the bucket belongs to no promotion and
        // carries no cohorte, so affectations would have nowhere to land while the file says the
        // students are somewhere.
        if (target.LevelId is not { } targetLevelId)
            return Result.Failure<BulkRosterAssignmentPlan>(
                BulkRosterAssignmentErrors.TargetIsUnassignedRoster(targetLabel));

        // Who the operator named, and a row for every line that named nobody. A separate question
        // from the one below, and one that fails for entirely unrelated reasons.
        var selection = await selectionResolver.ResolveAsync(targets, yearId, ct);
        var candidates = selection.RegistrationIds;
        var rows = selection.Unresolved.Select(Refuse).ToList();

        var registrations = await LoadRegistrationsAsync(candidates.Keys.ToList(), ct);
        var engagement    = await LoadEngagementAsync(registrations, ct);
        var stagesHeld    = await LoadStagesHeldAsync(registrations, ct);
        var targetStages  = await LoadTargetStagesAsync(targetGroupId, ct);

        var work = new List<PlannedRosterAssignment>();

        foreach (var registration in registrations
                     .OrderBy(r => r.CurrentGroupLabel)
                     .ThenBy(r => r.StudentName))
        {
            string? source = candidates[registration.Id];

            var (status, message) = Classify(
                registration, targetGroupId, targetLevelId, targetLabel,
                engagement, stagesHeld, targetStages);

            rows.Add(new BulkRosterAssignmentRow(
                registration.Id, registration.StudentName, registration.Cne, registration.Appogee,
                registration.CurrentGroupLabel, registration.AcademicGroupId, status, message, source));

            if (status.IsApplicable())
                work.Add(new PlannedRosterAssignment(
                    registration.Id, Joins: status == BulkRosterAssignmentRowStatus.WillJoin));
        }

        var report = BulkRosterAssignmentReport.From(
            targetGroupId, targetLabel, yearId, yearLabel, rows);

        return new BulkRosterAssignmentPlan(report, work, targetGroupId);
    }

    /// <summary>
    /// What would happen to one student — the whole decision, in one place, so the preview and the
    /// apply cannot read it differently.
    /// </summary>
    private static (BulkRosterAssignmentRowStatus Status, string Message) Classify(
        RegistrationInfo registration,
        int targetGroupId,
        int targetLevelId,
        string targetLabel,
        IReadOnlyDictionary<Guid, AffectationToll> engagement,
        IReadOnlyDictionary<Guid, IReadOnlyList<StageHeld>> stagesHeld,
        IReadOnlyDictionary<int, string> targetStages)
    {
        if (registration.AcademicGroupId == targetGroupId)
            return (BulkRosterAssignmentRowStatus.AlreadyThere,
                $"Déjà dans « {targetLabel} » : rien à faire.");

        // ⚠ Before the promotion check, because it is the stronger statement: a diplômé of the right
        // promotion is still someone nothing may be planned for, and naming the promotion instead
        // would send the operator to correct a list that is not wrong.
        if (registration.Status.EndsTheCursus())
            return (BulkRosterAssignmentRowStatus.CursusEnded,
                $"Le cursus de cet étudiant est terminé ({registration.Status}) : "
                + "aucun stage ne peut plus lui être planifié.");

        if (registration.LevelId != targetLevelId)
            return (BulkRosterAssignmentRowStatus.WrongPromotion,
                $"Cet étudiant n'est pas de la promotion de « {targetLabel} » : un groupe est "
                + "propre à une année et à une promotion.");

        if (registration.AcademicGroupId is null)
            return (BulkRosterAssignmentRowStatus.WillJoin,
                $"Sans groupe : il rejoint « {targetLabel} » et ses affectations y sont créées.");

        var toll = engagement.GetValueOrDefault(registration.Id, AffectationToll.None);

        if (toll.IsUnderway)
            return (BulkRosterAssignmentRowStatus.Underway,
                $"Déjà engagé dans « {registration.CurrentGroupLabel} » "
                + $"({toll.Periods} période(s), {toll.Started} commencée(s), {toll.Evaluated} évaluée(s), "
                + $"{toll.AttendanceDays} journée(s) de présence) : utilisez un transfert, qui garde la "
                + "trace du déplacement.");

        // The relocator refuses a target roster that cannot receive one of the student's stages, and
        // it refuses the whole act when it does. Answered here so the line names the student and the
        // stage instead of the batch dying on its first mover.
        var held = stagesHeld.GetValueOrDefault(registration.Id, []);
        var orphan = held.FirstOrDefault(s => !targetStages.ContainsKey(s.StageId));

        if (orphan is not null)
            return (BulkRosterAssignmentRowStatus.TargetMissingStage,
                $"« {targetLabel} » n'a pas de cohorte sur « {orphan.StageName} », où cet étudiant est "
                + "déjà affecté : créez les cohortes du groupe cible avant de l'y placer.");

        return (BulkRosterAssignmentRowStatus.WillMove,
            $"Déplacé depuis « {registration.CurrentGroupLabel} » vers « {targetLabel} », sans trace : "
            + "le dossier se lira comme s'il y avait toujours été.");
    }

    /// <summary>One unresolved line of the selection, in this act's vocabulary.</summary>
    private static BulkRosterAssignmentRow Refuse(UnresolvedTarget target) =>
        // Both roster fields are null: an unresolved line names nobody, so there is no roster to
        // leave — and no source for the report to collect.
        new(target.RegistrationId, target.StudentName, target.Cne, target.Appogee, null, null,
            target.Reason == TargetResolution.NotFound
                ? BulkRosterAssignmentRowStatus.NotFound
                : BulkRosterAssignmentRowStatus.WrongYear,
            target.Message, target.SourceIdentifier);

    private async Task<List<RegistrationInfo>> LoadRegistrationsAsync(
        IReadOnlyList<Guid> registrationIds, CancellationToken ct) =>
        registrationIds.Count == 0
            ? []
            : await RegistrationsQuery(dbContext, registrationIds).ToListAsync(ct);

    /// <summary>
    /// What the act needs to know about each selected registration, flat.
    /// </summary>
    /// <remarks>
    /// ⚠ Named and <c>internal static</c> so <c>SqlTranslationTests</c> can compile it without a
    /// database. The roster label comes through the optional navigation, which is the shape a
    /// provider has to left-join rather than refuse.
    /// </remarks>
    internal static IQueryable<RegistrationInfo> RegistrationsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> registrationIds) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => registrationIds.Contains(r.Id))
            .Select(r => new RegistrationInfo(
                r.Id,
                ((r.Student.FirstName ?? "") + " " + (r.Student.LastName ?? "")).Trim(),
                r.Student.CNE,
                r.Student.Appogee,
                r.LevelId,
                r.Status,
                r.AcademicGroupId,
                r.AcademicGroup != null ? r.AcademicGroup.Label : null));

    /// <summary>
    /// Each selected student's engagement <b>inside the roster he is in today</b> — the same scope
    /// <c>StudentGroupRelocator</c> refuses on.
    /// </summary>
    /// <remarks>
    /// ⚠ Two flat round trips rather than one query with nested aggregates. Folding a count over a
    /// collection navigation inside an aggregate over the affectations is the shape Npgsql refuses,
    /// which is why <c>AffectationTollReader</c> is built the same way. And ⚠ it is batched rather
    /// than asked per student: a hundred volunteers is a hundred round trips otherwise, on the act
    /// whose entire reason for existing is that a hundred of anything is too many.
    /// </remarks>
    private async Task<Dictionary<Guid, AffectationToll>> LoadEngagementAsync(
        IReadOnlyList<RegistrationInfo> registrations, CancellationToken ct)
    {
        var movers = registrations
            .Where(r => r.AcademicGroupId is not null)
            .ToList();

        if (movers.Count == 0)
            return [];

        var ids = movers.Select(r => r.Id).ToList();
        var sourceGroupOf = movers.ToDictionary(r => r.Id, r => r.AcademicGroupId!.Value);

        var assignmentCounts = await AssignmentCountsQuery(dbContext, ids).ToListAsync(ct);
        var periodCounts     = await PeriodCountsQuery(dbContext, ids).ToListAsync(ct);

        var byRegistration = new Dictionary<Guid, AffectationToll>();

        foreach (var (registrationId, sourceGroupId) in sourceGroupOf)
        {
            var a = assignmentCounts.FirstOrDefault(
                x => x.RegistrationId == registrationId && x.AcademicGroupId == sourceGroupId);
            var p = periodCounts.FirstOrDefault(
                x => x.RegistrationId == registrationId && x.AcademicGroupId == sourceGroupId);

            if (a is null && p is null)
                continue;

            byRegistration[registrationId] = new AffectationToll(
                a?.Total ?? 0, a?.Engaged ?? 0,
                p?.Periods ?? 0, p?.Started ?? 0, p?.Evaluated ?? 0, p?.AttendanceDays ?? 0);
        }

        return byRegistration;
    }

    internal static IQueryable<RosterAssignmentCounts> AssignmentCountsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> registrationIds) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => registrationIds.Contains(a.RegistrationId))
            .GroupBy(a => new { a.RegistrationId, a.Cohort.AcademicGroupId })
            .Select(g => new RosterAssignmentCounts(
                g.Key.RegistrationId,
                g.Key.AcademicGroupId,
                g.Count(),
                g.Count(a => a.Status != InternshipStatus.Planned)));

    internal static IQueryable<RosterPeriodCounts> PeriodCountsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> registrationIds) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => registrationIds.Contains(a.RegistrationId))
            .SelectMany(a => a.ServicePeriods)
            .GroupBy(p => new
            {
                p.InternshipAssignment.RegistrationId,
                p.InternshipAssignment.Cohort.AcademicGroupId,
            })
            .Select(g => new RosterPeriodCounts(
                g.Key.RegistrationId,
                g.Key.AcademicGroupId,
                g.Count(),
                g.Count(p => p.IsStarted),
                g.Count(p => p.Evaluation != null),
                g.Sum(p => p.Attendance.Count)));

    /// <summary>The stages each selected student already holds an affectation on.</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<StageHeld>>> LoadStagesHeldAsync(
        IReadOnlyList<RegistrationInfo> registrations, CancellationToken ct)
    {
        var ids = registrations.Where(r => r.AcademicGroupId is not null).Select(r => r.Id).ToList();
        if (ids.Count == 0)
            return [];

        var rows = await StagesHeldQuery(dbContext, ids).ToListAsync(ct);

        return rows
            .GroupBy(r => r.RegistrationId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<StageHeld>)[.. g.Select(r => new StageHeld(r.StageId, r.StageName)).DistinctBy(s => s.StageId)]);
    }

    internal static IQueryable<StageHeldRow> StagesHeldQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> registrationIds) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => registrationIds.Contains(a.RegistrationId))
            .Select(a => new StageHeldRow(a.RegistrationId, a.Cohort.StageId, a.Cohort.Stage.Name));

    /// <summary>The stages the target roster can receive — one cohorte each.</summary>
    private async Task<Dictionary<int, string>> LoadTargetStagesAsync(int targetGroupId, CancellationToken ct) =>
        await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.AcademicGroupId == targetGroupId)
            .Select(c => new { c.StageId, StageName = c.Stage.Name })
            .ToDictionaryAsync(x => x.StageId, x => x.StageName, ct);

    internal sealed record RegistrationInfo(
        Guid    Id,
        string  StudentName,
        string? Cne,
        string  Appogee,
        int     LevelId,
        RegistrationStatus Status,
        int?    AcademicGroupId,
        string? CurrentGroupLabel);

    internal sealed record RosterAssignmentCounts(
        Guid RegistrationId, int AcademicGroupId, int Total, int Engaged);

    internal sealed record RosterPeriodCounts(
        Guid RegistrationId, int AcademicGroupId,
        int Periods, int Started, int Evaluated, int AttendanceDays);

    internal sealed record StageHeldRow(Guid RegistrationId, int StageId, string StageName);

    internal sealed record StageHeld(int StageId, string StageName);
}
