using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GroupChange;

/// <summary>
/// « Changement de groupe » — moves one registration from the roster it is in to another, leaving
/// <b>no trace of the move</b>, so that afterwards the record reads exactly as it would have if the
/// répartition had put the student in the target roster from the start.
/// </summary>
/// <remarks>
/// <para><b>What it re-points, which is the whole substance of the act:</b>
/// <list type="bullet">
///   <item><c>Registration.AcademicGroupId</c> — the roster pointer.</item>
///   <item>every <c>InternshipAssignment.CurrentCohortId</c> the source roster held, onto the target
///   roster's cohorte <i>for the same stage</i>.</item>
///   <item>the open <c>CohortMembership</c> row, <b>rewritten in place</b> — no closing date and no
///   second row, which is what makes the move unreadable afterwards.</item>
///   <item>the périodes, rebuilt from the target cohorte's published cells so the student holds what
///   his new colleagues hold, folded the same way (<see cref="CohortMemberScheduler"/>).</item>
/// </list></para>
///
/// <para>⚠ <b>It is a correction, and correctness rests entirely on nothing having happened yet.</b>
/// The refusals are not ceremony: past a plan there is a rotation, a mark or a day of attendance
/// saying the student stood in the source roster's service, and « il n'y a jamais été » stops being a
/// correction. That case has an owner — <c>TransferStudentCommand</c> — which carries the running
/// rotation across and keeps the trace precisely because there is now something to trace. No force
/// flag exists here for the same reason <c>AcademicGroupErrors.RosterAffectationsUnderway</c> has
/// none: an act that destroys marks must not have a second, quieter door.</para>
///
/// <para>Shared by the single change and the échange, which is not a convenience: an échange <i>is</i>
/// two changements, and a second copy of this would eventually disagree with it about what « sans
/// trace » means.</para>
/// </remarks>
internal sealed class StudentGroupRelocator(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader,
    CohortMemberScheduler scheduler)
{
    /// <summary>
    /// Plans and applies one relocation. ⚠ Does <b>not</b> save — an échange is two of these and they
    /// land together or not at all.
    /// </summary>
    public async Task<Result<GroupChangeReport>> RelocateAsync(
        Guid registrationId, int targetGroupId, CancellationToken ct)
    {
        var registration = await dbContext.Registrations
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.Id == registrationId, ct);

        if (registration is null)
            return Result.Failure<GroupChangeReport>(RegistrationErrors.NotFound(registrationId));

        if (registration.AcademicGroupId is not { } sourceGroupId)
            return Result.Failure<GroupChangeReport>(GroupChangeErrors.NotInAGroup());

        var target = await RosterAsync(targetGroupId, ct);
        if (target is null)
            return Result.Failure<GroupChangeReport>(AcademicGroupErrors.NotFound(targetGroupId));

        string targetLabel = target.Label ?? $"Groupe {targetGroupId}";
        string sourceLabel = (await RosterAsync(sourceGroupId, ct))?.Label ?? $"Groupe {sourceGroupId}";

        if (sourceGroupId == targetGroupId)
            return Result.Failure<GroupChangeReport>(GroupChangeErrors.AlreadyInTargetGroup(targetLabel));

        // The two guards every write onto a roster pointer makes, and for the same reason: past this
        // point every check downstream is keyed on the roster the registration *claims*, so a roster of
        // another year or another promotion is never caught again.
        if (target.AcademicYearId != registration.AcademicYearId)
            return Result.Failure<GroupChangeReport>(AcademicGroupErrors.TargetGroupInAnotherYear(
                targetLabel,
                await YearLabelAsync(target.AcademicYearId, ct),
                await YearLabelAsync(registration.AcademicYearId, ct)));

        // ⚠ Unlike a transfer or a join, « Non réparti » is never a legitimate destination here: the
        // bucket carries no cohorte, so the affectations would have nowhere to land and would stay in
        // the source roster's cohortes while the file says the student is nowhere.
        if (target.LevelId is not { } targetLevel)
            return Result.Failure<GroupChangeReport>(
                GroupChangeErrors.TargetIsUnassignedRoster(targetLabel));

        if (targetLevel != registration.LevelId)
            return Result.Failure<GroupChangeReport>(AcademicGroupErrors.TargetGroupInAnotherLevel(
                targetLabel,
                await LevelLabelAsync(targetLevel, ct),
                await LevelLabelAsync(registration.LevelId, ct)));

        // Read from the store, not from the aggregate: a mark and a day of attendance hang off
        // collections an un-Included load reports as empty, and « rien enregistré » is the answer that
        // would wave through exactly the case this refuses. Same division as CnpnSpanFloor.
        var toll = await tollReader.ForRegistrationInRosterAsync(registrationId, sourceGroupId, ct);

        if (toll.IsUnderway)
            return Result.Failure<GroupChangeReport>(GroupChangeErrors.RotationsUnderway(
                sourceLabel, toll.Periods, toll.Started, toll.Evaluated, toll.AttendanceDays));

        var moving = await dbContext.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .Include(a => a.Cohort)
                .ThenInclude(c => c.Stage)
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.SlotCoverage)
            .Where(a => a.RegistrationId == registrationId && a.Cohort.AcademicGroupId == sourceGroupId)
            .ToListAsync(ct);

        var targetCohorts = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.AcademicGroupId == targetGroupId && c.Stage.LevelId == registration.LevelId)
            .Select(c => new { c.Id, c.StageId, StageName = c.Stage.Name })
            .ToListAsync(ct);

        var targetByStage = targetCohorts.ToDictionary(c => c.StageId);

        // Affectations this student already holds in the target roster — a revalidation placed there by
        // hand, or a move somebody started and did not finish. Read before anything is written, since
        // afterwards the moved rows would answer this question themselves.
        var occupiedTargets = (await dbContext.InternshipAssignments
                .AsNoTracking()
                .Where(a => a.RegistrationId == registrationId
                         && a.Cohort.AcademicGroupId == targetGroupId)
                .Select(a => a.CurrentCohortId)
                .ToListAsync(ct))
            .ToHashSet();

        foreach (var assignment in moving)
        {
            if (!targetByStage.TryGetValue(assignment.Cohort.StageId, out var landing))
                return Result.Failure<GroupChangeReport>(GroupChangeErrors.TargetRosterMissingStage(
                    targetLabel, assignment.Cohort.Stage.Name));

            if (occupiedTargets.Contains(landing.Id))
                return Result.Failure<GroupChangeReport>(GroupChangeErrors.AlreadyAffectedInTargetCohort(
                    targetLabel, landing.StageName));
        }

        foreach (var assignment in moving)
        {
            var reassigned = assignment.ReassignToCohort(targetByStage[assignment.Cohort.StageId].Id);
            if (reassigned.IsFailure)
                return Result.Failure<GroupChangeReport>(reassigned.Error);
        }

        registration.ReassignToGroup(targetGroupId);

        var created = CreateMissingAffectations(
            registration, moving, targetCohorts.ConvertAll(c => c.Id), occupiedTargets);

        var outcome = await scheduler.RematerializeAsync([.. moving, .. created], ct);

        return new GroupChangeReport(
            registrationId,
            $"{registration.Student.FirstName} {registration.Student.LastName}".Trim(),
            sourceLabel,
            targetLabel,
            moving.Count,
            created.Count,
            outcome.PeriodsCreated,
            outcome.PeriodsReplaced,
            outcome.AdHocPeriodsKept);
    }

    /// <summary>
    /// Cohortes of the target roster the student would otherwise not be in. A member of that roster
    /// holds one affectation per cohorte, and the promise of this act is that he is now such a member.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Built here rather than through <c>StudentAffectationService.AssignRegistrationAsync</c>,
    /// and the reason is a trap.</b> That method asks the store which cohortes the student is already
    /// in; the affectations just re-pointed above have <i>not been saved</i>, so the store still shows
    /// them in the source roster and it would create a second affectation for every stage — the
    /// duplication that made a re-découpage count students twice.</para>
    ///
    /// <para>The membership is dated with the earliest date the moved affectations carry, not with
    /// today: this act says the student was in the target roster from the start, and a row dated the
    /// day of the correction is precisely the trace it is meant not to leave.</para>
    /// </remarks>
    private List<InternshipAssignment> CreateMissingAffectations(
        Registration registration,
        IReadOnlyCollection<InternshipAssignment> moved,
        IReadOnlyCollection<int> targetCohortIds,
        IReadOnlyCollection<int> occupiedTargets)
    {
        var covered = moved.Select(a => a.CurrentCohortId).Concat(occupiedTargets).ToHashSet();

        var enrolledOn = moved
            .SelectMany(a => a.MembershipHistory)
            .Select(m => (DateOnly?)m.StartDate)
            .Min() ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var created = new List<InternshipAssignment>();

        foreach (int cohortId in targetCohortIds)
        {
            if (covered.Contains(cohortId)) continue;

            var assignmentId = Guid.NewGuid();
            var assignment = new InternshipAssignment
            {
                Id              = assignmentId,
                RegistrationId  = registration.Id,
                CurrentCohortId = cohortId,
            };

            assignment.MembershipHistory.Add(new CohortMembership
            {
                Id                     = Guid.NewGuid(),
                InternshipAssignmentId = assignmentId,
                CohortId               = cohortId,
                StartDate              = enrolledOn,
            });

            dbContext.InternshipAssignments.Add(assignment);
            created.Add(assignment);
        }

        return created;
    }

    private Task<RosterRef?> RosterAsync(int groupId, CancellationToken ct) =>
        dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new RosterRef(g.Label, g.AcademicYearId, g.LevelId))
            .FirstOrDefaultAsync(ct);

    private async Task<string> YearLabelAsync(int academicYearId, CancellationToken ct) =>
        await dbContext.AcademicYears
            .Where(y => y.Id == academicYearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(ct) ?? $"année {academicYearId}";

    private async Task<string> LevelLabelAsync(int levelId, CancellationToken ct) =>
        await dbContext.Levels
            .Where(l => l.Id == levelId)
            .Select(l => l.Label)
            .FirstOrDefaultAsync(ct) ?? $"niveau {levelId}";

    private sealed record RosterRef(string? Label, int AcademicYearId, int? LevelId);
}
