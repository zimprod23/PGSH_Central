using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Students.Registrations.Deliberation;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// PGSH covers stages only — it has no exams, no TP, no deliberation — so it cannot compute who
/// cleared a year. The verdict arrives from the faculty as a canvas, one per (year, level), and these
/// cover what that import must and must not do.
/// </summary>
public class DeliberationTests
{
    private static DeliberationPlanner Planner(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static ApplyDeliberationCommandHandler ApplyHandler(ApplicationDbContext db) =>
        new(db, Planner(db), db.AdminAuthorizer());

    private static PreviewDeliberationQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(Planner(db), db.AdminAuthorizer());

    /// <summary>A promotion of three, none of them deliberated yet.</summary>
    private static async Task<List<Registration>> SeedPromotionAsync(ApplicationDbContext db)
    {
        db.SeedCatalog();

        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        var students = new List<Registration>
        {
            db.SeedRegistration("Sara", "Bennani", group),
            db.SeedRegistration("Ali", "Amrani", group),
            db.SeedRegistration("Yasmine", "Idrissi", group),
        };

        await db.SaveChangesAsync();
        return students;
    }

    private static DeliberationRow Row(int sheetRow, Registration r, string? decision, string? motif = null) =>
        new(sheetRow, r.Student!.CNE, r.Student.Appogee, decision, motif);

    /// <summary>The classic canvas: one promotion, every student named, silence means silence.</summary>
    private static ApplyDeliberationCommand Apply(
        IReadOnlyList<DeliberationRow> rows, int? academicYearId = null) =>
        new(rows, TestHarness.LevelId, academicYearId);

    private static PreviewDeliberationQuery Preview(
        IReadOnlyList<DeliberationRow> rows, int? academicYearId = null) =>
        new(rows, TestHarness.LevelId, academicYearId);

    [Fact]
    public async Task Admis_redoublant_and_exclu_each_close_the_year_with_their_own_verdict()
    {
        await using var db = TestHarness.NewContext(nameof(Admis_redoublant_and_exclu_each_close_the_year_with_their_own_verdict));
        var promotion = await SeedPromotionAsync(db);

        var result = await ApplyHandler(db).Handle(
            Apply( [
                Row(2, promotion[0], "Admis"),
                Row(3, promotion[1], "Redoublant", "Deux modules non acquis"),
                Row(4, promotion[2], "Exclu", "Troisième redoublement"),
            ]),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WillRecord.Should().Be(3);
        result.Value.ErrorCount.Should().Be(0);

        var closed = await db.Registrations.OrderBy(r => r.Status).ToListAsync();
        closed.Should().AllSatisfy(r =>
        {
            r.OutcomeSource.Should().Be(RegistrationOutcomeSource.Declared);
            r.OutcomeRecordedOn.Should().NotBeNull();
        });

        closed.Select(r => r.Status).Should().BeEquivalentTo([
            RegistrationStatus.Validated, RegistrationStatus.Failed, RegistrationStatus.Excluded,
        ]);
    }

    [Fact]
    public async Task A_motif_is_kept_on_an_adverse_verdict_and_dropped_on_a_favourable_one()
    {
        await using var db = TestHarness.NewContext(nameof(A_motif_is_kept_on_an_adverse_verdict_and_dropped_on_a_favourable_one));
        var promotion = await SeedPromotionAsync(db);

        var result = await ApplyHandler(db).Handle(
            Apply( [
                Row(2, promotion[0], "Admis", "félicitations du jury"),
                Row(3, promotion[1], "Redoublant", "Deux modules non acquis"),
            ]),
            default);

        result.IsSuccess.Should().BeTrue();

        var admis = await db.Registrations.FirstAsync(r => r.StudentId == promotion[0].StudentId);
        var redoublant = await db.Registrations.FirstAsync(r => r.StudentId == promotion[1].StudentId);

        admis.failureReasons.Should().BeNull();
        redoublant.failureReasons!.Description.Should().Be("Deux modules non acquis");

        // Dropping it silently is the failure mode this guards: the row has to say so.
        result.Value.Rows.Single(r => r.SheetRow == 2).Message.Should().Contain("Motif ignoré");
    }

    [Fact]
    public async Task One_unrecognised_decision_refuses_the_whole_file()
    {
        await using var db = TestHarness.NewContext(nameof(One_unrecognised_decision_refuses_the_whole_file));
        var promotion = await SeedPromotionAsync(db);

        var result = await ApplyHandler(db).Handle(
            Apply( [
                Row(2, promotion[0], "Admis"),
                Row(3, promotion[1], "Peut-être"),
            ]),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliberation.Rejected");

        // A promotion half closed is unreconcilable, so the good row must not have landed either.
        var untouched = await db.Registrations.ToListAsync();
        untouched.Should().AllSatisfy(r => r.OutcomeSource.Should().BeNull());
    }

    [Fact]
    public async Task An_unknown_identifier_is_reported_against_its_own_row()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_identifier_is_reported_against_its_own_row));
        var promotion = await SeedPromotionAsync(db);

        var report = await PreviewHandler(db).Handle(
            Preview( [
                Row(2, promotion[0], "Admis"),
                new DeliberationRow(3, "CNE-INCONNU", null, "Admis", null),
            ]),
            default);

        report.IsSuccess.Should().BeTrue();
        report.Value.CanApply.Should().BeFalse();
        report.Value.Rows.Single(r => r.SheetRow == 3).Status
            .Should().Be(DeliberationRowStatus.UnknownStudent);
    }

    [Fact]
    public async Task The_same_student_twice_in_one_file_is_a_duplicate()
    {
        await using var db = TestHarness.NewContext(nameof(The_same_student_twice_in_one_file_is_a_duplicate));
        var promotion = await SeedPromotionAsync(db);

        var report = await PreviewHandler(db).Handle(
            Preview( [
                Row(2, promotion[0], "Admis"),
                Row(3, promotion[0], "Redoublant"),
            ]),
            default);

        report.Value.Rows.Single(r => r.SheetRow == 3).Status
            .Should().Be(DeliberationRowStatus.DuplicateStudent);
        report.Value.CanApply.Should().BeFalse();
    }

    [Fact]
    public async Task Decisions_are_matched_whatever_the_accents_and_casing()
    {
        await using var db = TestHarness.NewContext(nameof(Decisions_are_matched_whatever_the_accents_and_casing));
        var promotion = await SeedPromotionAsync(db);

        var report = await PreviewHandler(db).Handle(
            Preview( [
                Row(2, promotion[0], "  ADMIS "),
                Row(3, promotion[1], "ajourné"),
                Row(4, promotion[2], "Démission"),
            ]),
            default);

        report.Value.CanApply.Should().BeTrue();
        report.Value.Rows.Select(r => r.Outcome).Should().BeEquivalentTo([
            RegistrationStatus.Validated, RegistrationStatus.Failed, RegistrationStatus.Withdrawn,
        ]);
    }

    [Fact]
    public async Task Re_uploading_a_corrected_file_replaces_the_verdict_it_already_recorded()
    {
        await using var db = TestHarness.NewContext(nameof(Re_uploading_a_corrected_file_replaces_the_verdict_it_already_recorded));
        var promotion = await SeedPromotionAsync(db);

        await ApplyHandler(db).Handle(
            Apply( [Row(2, promotion[0], "Redoublant", "erreur de saisie")]),
            default);

        var corrected = await ApplyHandler(db).Handle(
            Apply( [Row(2, promotion[0], "Admis")]),
            default);

        corrected.IsSuccess.Should().BeTrue();
        corrected.Value.WillReplace.Should().Be(1);
        corrected.Value.WillRecord.Should().Be(0);

        var registration = await db.Registrations.FirstAsync(r => r.StudentId == promotion[0].StudentId);
        registration.Status.Should().Be(RegistrationStatus.Validated);
        registration.failureReasons.Should().BeNull();
    }

    [Fact]
    public async Task Students_the_file_never_mentions_are_counted_and_left_alone()
    {
        await using var db = TestHarness.NewContext(nameof(Students_the_file_never_mentions_are_counted_and_left_alone));
        var promotion = await SeedPromotionAsync(db);

        var report = await PreviewHandler(db).Handle(
            Preview( [Row(2, promotion[0], "Admis")]),
            default);

        // A promotion of three closed with a one-row file is worth seeing before applying.
        report.Value.NotCovered.Should().Be(2);
        report.Value.CanApply.Should().BeTrue();
    }

    [Fact]
    public async Task Diplome_on_a_level_that_is_not_the_last_of_the_students_CNPN_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Diplome_on_a_level_that_is_not_the_last_of_the_students_CNPN_is_refused));
        var promotion = await SeedPromotionAsync(db);

        // SeedCatalog's level is the 3rd year; the text in force runs six.
        promotion[0].Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        var report = await PreviewHandler(db).Handle(
            Preview( [Row(2, promotion[0], "Diplômé")]),
            default);

        report.Value.Rows.Single().Status.Should().Be(DeliberationRowStatus.NotAFinalYear);
        report.Value.CanApply.Should().BeFalse();
    }

    [Fact]
    public async Task Diplome_stands_aside_where_the_student_carries_no_CNPN_stamp()
    {
        await using var db = TestHarness.NewContext(nameof(Diplome_stands_aside_where_the_student_carries_no_CNPN_stamp));
        var promotion = await SeedPromotionAsync(db);

        // ~2,200 stamps are inferred and 19 students have none at all; refusing on absence would make
        // the feature unusable on the real data.
        var report = await PreviewHandler(db).Handle(
            Preview( [Row(2, promotion[0], "Diplômé")]),
            default);

        report.Value.CanApply.Should().BeTrue();
        report.Value.Rows.Single().Outcome.Should().Be(RegistrationStatus.Graduated);
    }

    [Fact]
    public async Task An_admis_whose_stage_is_not_validated_is_flagged_but_never_blocked()
    {
        await using var db = TestHarness.NewContext(nameof(An_admis_whose_stage_is_not_validated_is_flagged_but_never_blocked));
        var stage = db.SeedCatalog();
        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        var service = db.SeedService(2, "Cardiologie");
        var registration = db.SeedRegistration("Sara", "Bennani", group);
        var cohort = db.SeedCohortFor(stage, group, cohortId: 30);

        db.SeedGradedAssignment(registration, cohort, service, mark: 7);
        await db.SaveChangesAsync();

        var report = await PreviewHandler(db).Handle(
            Preview( [Row(2, registration, "Admis")]),
            default);

        // The jury deliberates on the whole year; PGSH sees only the stages. It reports, it does not rule.
        report.Value.ContradictionCount.Should().Be(1);
        report.Value.Rows.Single().HasUnvalidatedStages.Should().BeTrue();
        report.Value.CanApply.Should().BeTrue();
    }

    [Fact]
    public async Task A_caller_who_is_not_administrative_cannot_close_a_year()
    {
        await using var db = TestHarness.NewContext(nameof(A_caller_who_is_not_administrative_cannot_close_a_year));
        var promotion = await SeedPromotionAsync(db);

        var handler = new ApplyDeliberationCommandHandler(db, Planner(db), db.StrangerAuthorizer());

        var result = await handler.Handle(
            Apply( [Row(2, promotion[0], "Admis")]),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliberation.NotAllowed");
    }

    [Fact]
    public async Task A_year_the_level_never_ran_refuses_rather_than_returning_an_empty_canvas()
    {
        await using var db = TestHarness.NewContext(nameof(A_year_the_level_never_ran_refuses_rather_than_returning_an_empty_canvas));
        await SeedPromotionAsync(db);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        await db.SaveChangesAsync();

        var report = await PreviewHandler(db).Handle(
            Preview(
                [new DeliberationRow(2, "X", null, "Admis", null)],
                TestHarness.PreviousYearId),
            default);

        report.IsFailure.Should().BeTrue();
        report.Error.Code.Should().Be("Deliberation.PromotionHasNoStudents");
    }

    [Fact]
    public async Task The_canvas_scopes_to_one_promotion_and_not_to_every_year_the_level_ever_ran()
    {
        await using var db = TestHarness.NewContext(nameof(The_canvas_scopes_to_one_promotion_and_not_to_every_year_the_level_ever_ran));
        db.SeedCatalog();
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        db.SeedRegistration("Sara", "Bennani", group);
        var lastYear = db.SeedRegistration("Ali", "Amrani", null, TestHarness.PreviousYearId);
        await db.SaveChangesAsync();

        // Ali is at the same level, in the year before. The current-year canvas must not reach him.
        var report = await PreviewHandler(db).Handle(
            Preview( [Row(2, lastYear, "Admis")]),
            default);

        report.Value.Rows.Single().Status.Should().Be(DeliberationRowStatus.UnknownStudent);
    }

    [Fact]
    public async Task An_inferred_verdict_can_never_overwrite_one_the_faculty_declared()
    {
        await using var db = TestHarness.NewContext(nameof(An_inferred_verdict_can_never_overwrite_one_the_faculty_declared));
        var promotion = await SeedPromotionAsync(db);

        await ApplyHandler(db).Handle(
            Apply( [Row(2, promotion[0], "Admis")]),
            default);

        var registration = await db.Registrations.FirstAsync(r => r.StudentId == promotion[0].StudentId);

        // This is the guard Phase 14.3's inference will run into when it back-fills the imported years.
        var overwritten = registration.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Inferred, null, DateTime.UtcNow);

        overwritten.IsFailure.Should().BeTrue();
        overwritten.Error.Code.Should().Be("Registrations.OutcomeAlreadyDeclared");
        registration.Status.Should().Be(RegistrationStatus.Validated);
    }

    [Fact]
    public void A_year_still_running_is_not_a_verdict_a_deliberation_can_pronounce()
    {
        var registration = new Registration { Id = Guid.NewGuid(), Status = RegistrationStatus.Active };

        var result = registration.RecordYearOutcome(
            RegistrationStatus.Active, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.NotAYearOutcome");
        registration.OutcomeSource.Should().BeNull();
    }
}
