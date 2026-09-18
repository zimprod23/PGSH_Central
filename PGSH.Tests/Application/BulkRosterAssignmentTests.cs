using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.BulkAssignment;
using PGSH.Application.Students.Selection;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Voici la liste des volontaires, mettez-les dans ce groupe » — in one act instead of one dialog
/// per student.
/// </summary>
/// <remarks>
/// <para>The act is the composition step of a nominative placement: a partner hospital takes the
/// students who volunteered for it, and the answer to a nominative request is a <b>roster</b>. What
/// happens afterwards — pinning the roster's cells onto the reserved service — already worked; this
/// is the half that was done a hundred times by hand.</para>
///
/// <para>⚠ Two verbs, decided per student. A registration in no roster is <i>attached</i>; one already
/// in a roster is <i>moved</i>, through the same relocator « changement de groupe » runs. Everything
/// the two single acts refuse is refused here too, per line, and the rest is still written.</para>
/// </remarks>
public class BulkRosterAssignmentTests
{
    private const int SourceGroupId = 10;
    private const int TargetGroupId = 20;
    private const int SourceCohortId = 101;
    private const int TargetCohortId = 102;
    private const int ServiceId = 30;

    private static readonly DateOnly PeriodStart = new(2025, 10, 1);
    private static readonly DateOnly PeriodEnd = new(2025, 10, 31);

    private sealed record Scenario(
        Stage Stage, AcademicGroup Source, AcademicGroup Target, Service Service);

    /// <summary>
    /// One stage, a source roster and a target roster, both carrying a cohorte on that stage — so a
    /// move has somewhere to land, which is the ordinary case.
    /// </summary>
    private static Scenario Seed(ApplicationDbContext db, bool targetHasCohort = true)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");

        var source = db.SeedGroup(SourceGroupId, SourceGroupId);
        var target = db.SeedGroup(TargetGroupId, TargetGroupId);

        db.SeedCohortFor(stage, source, SourceCohortId);
        if (targetHasCohort)
            db.SeedCohortFor(stage, target, TargetCohortId);

        return new Scenario(stage, source, target, service);
    }

    private static ApplyBulkRosterAssignmentCommand Apply(StudentTargets targets, int confirmed) =>
        new(TargetGroupId, targets, confirmed, Reason: "Volontaires Kénitra (GST)");

    private static PreviewBulkRosterAssignmentQuery Preview(StudentTargets targets) =>
        new(TargetGroupId, targets);

    [Fact]
    public async Task A_student_in_no_roster_joins_and_receives_the_target_cohortes_affectations()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_in_no_roster_joins_and_receives_the_target_cohortes_affectations));
        Seed(db);
        var student = db.SeedRegistration("Amine", "Bennani");
        await db.SaveChangesAsync();

        var targets = new StudentTargets(RegistrationIds: [student.Id]);

        var preview = await db.AssignToRosterPreview().Handle(Preview(targets), default);
        preview.Value.JoinCount.Should().Be(1);
        preview.Value.ApplicableCount.Should().Be(1);

        var applied = await db.AssignToRosterHandler().Handle(Apply(targets, confirmed: 1), default);
        applied.IsSuccess.Should().BeTrue();

        var reloaded = await db.Registrations.FirstAsync(r => r.Id == student.Id);
        reloaded.AcademicGroupId.Should().Be(TargetGroupId);

        (await db.InternshipAssignments.CountAsync(a => a.RegistrationId == student.Id))
            .Should().Be(1, "joining a roster creates the affectations of its cohortes");
    }

    [Fact]
    public async Task A_student_in_another_roster_is_moved_without_a_trace()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_in_another_roster_is_moved_without_a_trace));
        var s = Seed(db);
        var student = db.SeedRegistration("Salma", "Idrissi", s.Source);
        var cohort = await Task.FromResult(db.Cohorts.Local.First(c => c.Id == SourceCohortId));
        db.SeedAssignment(student, cohort);
        await db.SaveChangesAsync();

        var targets = new StudentTargets(RegistrationIds: [student.Id]);

        var preview = await db.AssignToRosterPreview().Handle(Preview(targets), default);
        preview.Value.MoveCount.Should().Be(1);

        var applied = await db.AssignToRosterHandler().Handle(Apply(targets, confirmed: 1), default);
        applied.IsSuccess.Should().BeTrue();

        var reloaded = await db.Registrations.FirstAsync(r => r.Id == student.Id);
        reloaded.AcademicGroupId.Should().Be(TargetGroupId);

        var assignment = await db.InternshipAssignments
            .Include(a => a.MembershipHistory)
            .FirstAsync(a => a.RegistrationId == student.Id);

        assignment.CurrentCohortId.Should().Be(TargetCohortId, "the affectation follows the student");
        assignment.MembershipHistory.Should().HaveCount(1,
            "« sans trace » means the membership row is rewritten in place, never closed and doubled");
        assignment.MembershipHistory.Single().CohortId.Should().Be(TargetCohortId);
    }

    [Fact]
    public async Task A_whole_roster_is_named_by_its_id_and_a_pasted_list_by_CNE_or_Apogee()
    {
        await using var db = TestHarness.NewContext(nameof(A_whole_roster_is_named_by_its_id_and_a_pasted_list_by_CNE_or_Apogee));
        var s = Seed(db);
        var inRoster = db.SeedRegistration("Youssef", "Alaoui", s.Source);
        var byCne = db.SeedRegistration("Nadia", "Cherkaoui");
        var byApogee = db.SeedRegistration("Omar", "Tazi");
        await db.SaveChangesAsync();

        // ⚠ Lowered on purpose: the Apogée match was case-sensitive for months, so « ap2200a » never
        // found AP2200A.
        var targets = new StudentTargets(
            AcademicGroupIds: [SourceGroupId],
            Identifiers: [byCne.Student.CNE!.ToLowerInvariant(), byApogee.Student.Appogee.ToLowerInvariant()]);

        var preview = await db.AssignToRosterPreview().Handle(Preview(targets), default);
        preview.Value.ApplicableCount.Should().Be(3);

        var applied = await db.AssignToRosterHandler().Handle(Apply(targets, confirmed: 3), default);
        applied.IsSuccess.Should().BeTrue();

        (await db.Registrations.CountAsync(r => r.AcademicGroupId == TargetGroupId)).Should().Be(3);
        _ = inRoster;
    }

    [Fact]
    public async Task A_started_rotation_is_refused_by_name_and_the_others_are_still_written()
    {
        await using var db = TestHarness.NewContext(nameof(A_started_rotation_is_refused_by_name_and_the_others_are_still_written));
        var s = Seed(db);

        var engaged = db.SeedRegistration("Karim", "Fassi", s.Source);
        var free = db.SeedRegistration("Leila", "Ouazzani", s.Source);
        var cohort = db.Cohorts.Local.First(c => c.Id == SourceCohortId);

        var engagedAssignment = db.SeedAssignment(engaged, cohort);
        db.SeedPeriod(engagedAssignment, s.Service, PeriodStart, PeriodEnd, started: true);
        db.SeedAssignment(free, cohort);
        await db.SaveChangesAsync();

        var targets = new StudentTargets(AcademicGroupIds: [SourceGroupId]);

        var preview = await db.AssignToRosterPreview().Handle(Preview(targets), default);
        preview.Value.ApplicableCount.Should().Be(1);
        preview.Value.RefusedCount.Should().Be(1);

        var refused = preview.Value.Rows.Single(r => r.RegistrationId == engaged.Id);
        refused.Status.Should().Be(BulkRosterAssignmentRowStatus.Underway);
        refused.Message.Should().Contain("transfert",
            "the refusal names the act that can do it, which is the whole point of refusing by name");

        var applied = await db.AssignToRosterHandler().Handle(Apply(targets, confirmed: 1), default);
        applied.IsSuccess.Should().BeTrue();

        (await db.Registrations.FirstAsync(r => r.Id == free.Id)).AcademicGroupId.Should().Be(TargetGroupId);
        (await db.Registrations.FirstAsync(r => r.Id == engaged.Id)).AcademicGroupId.Should().Be(SourceGroupId);
    }

    [Fact]
    public async Task A_student_of_another_promotion_is_refused_because_a_roster_is_keyed_on_one()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_of_another_promotion_is_refused_because_a_roster_is_keyed_on_one));
        Seed(db);
        var otherLevel = db.SeedLevel(99, "4ème année", 4);
        var stranger = db.SeedRegistration("Hamza", "Rachidi", levelId: otherLevel.Id);
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview()
            .Handle(Preview(new StudentTargets(RegistrationIds: [stranger.Id])), default);

        preview.Value.ApplicableCount.Should().Be(0);
        preview.Value.Rows.Single().Status.Should().Be(BulkRosterAssignmentRowStatus.WrongPromotion);
    }

    [Fact]
    public async Task A_finished_cursus_is_refused_before_the_promotion_is_even_looked_at()
    {
        await using var db = TestHarness.NewContext(nameof(A_finished_cursus_is_refused_before_the_promotion_is_even_looked_at));
        Seed(db);
        var graduate = db.SeedRegistration("Fatima", "Zahra");
        graduate.Status = RegistrationStatus.Graduated;
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview()
            .Handle(Preview(new StudentTargets(RegistrationIds: [graduate.Id])), default);

        preview.Value.Rows.Single().Status.Should().Be(BulkRosterAssignmentRowStatus.CursusEnded);
    }

    [Fact]
    public async Task A_target_roster_with_no_cohorte_for_a_stage_the_student_holds_is_named()
    {
        await using var db = TestHarness.NewContext(nameof(A_target_roster_with_no_cohorte_for_a_stage_the_student_holds_is_named));
        var s = Seed(db, targetHasCohort: false);
        var student = db.SeedRegistration("Reda", "Amrani", s.Source);
        db.SeedAssignment(student, db.Cohorts.Local.First(c => c.Id == SourceCohortId));
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview()
            .Handle(Preview(new StudentTargets(RegistrationIds: [student.Id])), default);

        // ⚠ Answered per line rather than by letting the relocator fail the whole batch on its first
        // mover: the message names the student and the stage, and the fix is above them both.
        var row = preview.Value.Rows.Single();
        row.Status.Should().Be(BulkRosterAssignmentRowStatus.TargetMissingStage);
        row.Message.Should().Contain("Cardiologie");
    }

    [Fact]
    public async Task Already_in_the_target_is_its_own_answer_and_not_a_refusal()
    {
        await using var db = TestHarness.NewContext(nameof(Already_in_the_target_is_its_own_answer_and_not_a_refusal));
        var s = Seed(db);
        var settled = db.SeedRegistration("Imane", "Berrada", s.Target);
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview()
            .Handle(Preview(new StudentTargets(RegistrationIds: [settled.Id])), default);

        // Re-sending a corrected list is the normal way this act is used, so most of a second run
        // lands here. Read as a refusal it would look like the run failed.
        preview.Value.AlreadyThereCount.Should().Be(1);
        preview.Value.RefusedCount.Should().Be(0);
        preview.Value.ApplicableCount.Should().Be(0);
    }

    [Fact]
    public async Task An_identifier_nobody_carries_comes_back_as_a_row_never_as_silence()
    {
        await using var db = TestHarness.NewContext(nameof(An_identifier_nobody_carries_comes_back_as_a_row_never_as_silence));
        Seed(db);
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview()
            .Handle(Preview(new StudentTargets(Identifiers: ["CNE-QUI-NEXISTE-PAS"])), default);

        var row = preview.Value.Rows.Single();
        row.Status.Should().Be(BulkRosterAssignmentRowStatus.NotFound);
        row.SourceIdentifier.Should().Be("CNE-QUI-NEXISTE-PAS",
            "an unmatched line has to be findable in the file it was typed in");
    }

    [Fact]
    public async Task A_student_registered_in_another_year_is_told_so_rather_than_reported_missing()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_registered_in_another_year_is_told_so_rather_than_reported_missing));
        Seed(db);
        var past = db.SeedAcademicYear(
            9, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        var elsewhere = db.SeedRegistration("Hind", "Mansouri", academicYearId: past.Id);
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview()
            .Handle(Preview(new StudentTargets(Identifiers: [elsewhere.Student.Appogee])), default);

        // « Je ne le trouve pas » and « il est sur une autre année » are two different corrections.
        preview.Value.Rows.Single().Status.Should().Be(BulkRosterAssignmentRowStatus.WrongYear);
    }

    [Fact]
    public async Task The_apply_refuses_on_a_count_that_no_longer_matches_and_writes_nothing()
    {
        await using var db = TestHarness.NewContext(nameof(The_apply_refuses_on_a_count_that_no_longer_matches_and_writes_nothing));
        var s = Seed(db);
        db.SeedRegistration("Sara", "Lahlou", s.Source);
        db.SeedRegistration("Mehdi", "Benjelloun", s.Source);
        await db.SaveChangesAsync();

        // The operator previewed one student; a second joined the roster before he clicked.
        var applied = await db.AssignToRosterHandler()
            .Handle(Apply(new StudentTargets(AcademicGroupIds: [SourceGroupId]), confirmed: 1), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("RosterAssignment.CountMismatch");
        applied.Error.Description.Should().Contain("2").And.Contain("1");

        (await db.Registrations.CountAsync(r => r.AcademicGroupId == TargetGroupId)).Should().Be(0,
            "a refusal ordered after the write would return the same failure and pass a handler test");
    }

    [Fact]
    public async Task A_selection_naming_nobody_is_refused_rather_than_answered_with_an_empty_report()
    {
        await using var db = TestHarness.NewContext(nameof(A_selection_naming_nobody_is_refused_rather_than_answered_with_an_empty_report));
        Seed(db);
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview().Handle(Preview(new StudentTargets()), default);

        // « Personne n'est désigné » and « personne n'est concerné » are the same zero and opposite
        // situations: a request that lost its payload, and a list already applied.
        preview.IsFailure.Should().BeTrue();
        preview.Error.Code.Should().Be("RosterAssignment.NamesNobody");
    }

    [Fact]
    public async Task The_unassigned_bucket_is_never_a_destination()
    {
        await using var db = TestHarness.NewContext(nameof(The_unassigned_bucket_is_never_a_destination));
        var s = Seed(db);
        var bucket = db.SeedUnassignedBucket(90);
        var student = db.SeedRegistration("Anas", "Sebti", s.Source);
        await db.SaveChangesAsync();

        var preview = await db.AssignToRosterPreview().Handle(
            new PreviewBulkRosterAssignmentQuery(90, new StudentTargets(RegistrationIds: [student.Id])),
            default);

        preview.IsFailure.Should().BeTrue();
        preview.Error.Code.Should().Be("RosterAssignment.TargetIsUnassignedRoster");
    }

    [Fact]
    public async Task Only_the_administration_may_run_it()
    {
        await using var db = TestHarness.NewContext(nameof(Only_the_administration_may_run_it));
        var s = Seed(db);
        var student = db.SeedRegistration("Zineb", "Kabbaj", s.Source);
        await db.SaveChangesAsync();

        var targets = new StudentTargets(RegistrationIds: [student.Id]);

        var applied = await db.AssignToRosterHandler(db.StrangerAuthorizer())
            .Handle(Apply(targets, confirmed: 1), default);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("RosterAssignment.NotAllowed");
        (await db.Registrations.FirstAsync(r => r.Id == student.Id)).AcademicGroupId.Should().Be(SourceGroupId);
    }

    /// <summary>
    /// The rosters the act empties, named by the report — because only the server knows them.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>This exists for a cache, and that is not a small thing.</b> Both roster detail pages
    /// go stale on a move, and the client cannot name the ones it is emptying: a selection may be a
    /// whole promotion given by roster id or by a pasted list, and it is the server that read each
    /// registration's <c>AcademicGroupId</c>. Named by nobody, the source page goes on listing
    /// students who have left — which reads as an act that did nothing.</para>
    ///
    /// <para>⚠ <b>And it is measured before the display cap.</b> A source read off <c>Rows</c> would
    /// silently omit every roster whose lines were cut, exactly as a count computed off that list
    /// reads low.</para>
    /// </remarks>
    [Fact]
    public async Task The_report_names_the_rosters_the_act_empties_and_only_those()
    {
        await using var db = TestHarness.NewContext(nameof(The_report_names_the_rosters_the_act_empties_and_only_those));
        var s = Seed(db);

        // A second roster, so « the sources » is a set and not a single value dressed up as one.
        var other = db.SeedGroup(11, 11);
        db.SeedCohortFor(s.Stage, other, 103);

        var moved      = db.SeedRegistration("Salma", "Idrissi", s.Source);
        var movedToo   = db.SeedRegistration("Karim", "Benali", other);
        var joining    = db.SeedRegistration("Nadia", "Alaoui");
        var alreadyIn  = db.SeedRegistration("Youssef", "Tazi", s.Target);
        await db.SaveChangesAsync();

        var targets = new StudentTargets(
            RegistrationIds: [moved.Id, movedToo.Id, joining.Id, alreadyIn.Id]);

        var preview = await db.AssignToRosterPreview().Handle(Preview(targets), default);

        preview.Value.SourceGroupIds.Should().BeEquivalentTo([SourceGroupId, 11],
            "only a move leaves a roster — the student who joins comes from none, and the one already "
            + "in the target changes nothing");

        preview.Value.SourceGroupIds.Should().NotContain(TargetGroupId,
            "the destination is not a roster the act empties");
    }
}
