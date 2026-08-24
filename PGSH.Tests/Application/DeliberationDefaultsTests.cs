using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Students.Registrations.Deliberation;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The exceptions canvas: a file that names only the students the year went badly for, read against a
/// whole academic year rather than one promotion. Silence is a verdict here, which is the entire point
/// and the entire risk — so what these cover is mostly <em>what the default refuses to touch</em>.
/// </summary>
public class DeliberationDefaultsTests
{
    private const int SixthYearLevelId = 6;

    private static DeliberationPlanner Planner(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static ApplyDeliberationCommandHandler ApplyHandler(ApplicationDbContext db) =>
        new(db, Planner(db), db.AdminAuthorizer());

    private static PreviewDeliberationQueryHandler PreviewHandler(ApplicationDbContext db) =>
        new(Planner(db), db.AdminAuthorizer());

    private static DeliberationRow Row(int sheetRow, Registration r, string? decision, string? motif = null) =>
        new(sheetRow, r.Student!.CNE, r.Student.Appogee, decision, motif);

    /// <summary>An exceptions file over the whole year, with the default on.</summary>
    private static PreviewDeliberationQuery Preview(IReadOnlyList<DeliberationRow> rows, int? levelId = null) =>
        new(rows, levelId, null, DefaultUnlistedToAdmis: true);

    private static ApplyDeliberationCommand Apply(
        IReadOnlyList<DeliberationRow> rows, int? confirmed, int? levelId = null) =>
        new(rows, levelId, null, DefaultUnlistedToAdmis: true, ConfirmedDefaultCount: confirmed);

    [Fact]
    public async Task Everyone_the_file_does_not_name_is_admitted()
    {
        await using var db = TestHarness.NewContext(nameof(Everyone_the_file_does_not_name_is_admitted));
        db.SeedCatalog();
        var group = db.SeedGroup(groupId: 10, groupNumber: 10);

        var redoublant = db.SeedRegistration("Ali", "Amrani", group);
        var first = db.SeedRegistration("Sara", "Bennani", group);
        var second = db.SeedRegistration("Yasmine", "Idrissi", group);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, redoublant, "Redoublant", "Deux modules non acquis")], confirmed: 2),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DefaultedCount.Should().Be(2);
        result.Value.TotalRows.Should().Be(1);

        var stored = await db.Registrations.ToListAsync();
        stored.Single(r => r.Id == redoublant.Id).Status.Should().Be(RegistrationStatus.Failed);

        // Admitted by silence, and stamped Declared like any other verdict — the réinscription reads
        // OutcomeSource, and a default that left it null would create nothing for these two.
        foreach (var promoted in new[] { first, second })
        {
            var row = stored.Single(r => r.Id == promoted.Id);
            row.Status.Should().Be(RegistrationStatus.Validated);
            row.OutcomeSource.Should().Be(RegistrationOutcomeSource.Declared);
        }
    }

    [Fact]
    public async Task A_year_that_may_be_the_students_last_is_left_undecided_and_the_other_text_is_promoted()
    {
        await using var db = TestHarness.NewContext(
            nameof(A_year_that_may_be_the_students_last_is_left_undecided_and_the_other_text_is_promoted));
        db.SeedCatalog();
        db.SeedLevel(SixthYearLevelId, "6ème année", year: 6);

        // ⚠ The case the level alone cannot answer: from 2026-2027 one 6ème année holds both texts.
        var sixYearText = db.SeedRegistration("Sara", "Bennani", levelId: SixthYearLevelId);
        var sevenYearText = db.SeedRegistration("Ali", "Amrani", levelId: SixthYearLevelId);
        var listed = db.SeedRegistration("Yasmine", "Idrissi", levelId: SixthYearLevelId);

        sixYearText.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        sevenYearText.Student!.AssignCnpnVersion(TestHarness.OldCnpnId, isInferred: false);
        listed.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, listed, "Redoublant")], confirmed: 1),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.FinalYearUndecidedCount.Should().Be(1);
        result.Value.DefaultedCount.Should().Be(1);

        var stored = await db.Registrations.ToListAsync();

        // ⚠ Measured on the real base 2026-08-18: 855 of the 1 657 students in 7ème année Médecine had
        // been in the 7ème année before, 132 of them four times. The final year is the thesis year —
        // staying is as ordinary as finishing, and PGSH holds no record of a defence — so silence
        // decides nothing there. Reading it as « diplômé » graduated ~930 people still enrolled.
        var undecided = stored.Single(r => r.Id == sixYearText.Id);
        undecided.OutcomeSource.Should().BeNull();
        undecided.Status.Should().NotBe(RegistrationStatus.Graduated);

        // The seven-year student in the same room has a year above him, so he is simply promoted.
        stored.Single(r => r.Id == sevenYearText.Id).Status.Should().Be(RegistrationStatus.Validated);
    }

    [Fact]
    public async Task A_graduation_named_in_the_file_is_still_recorded()
    {
        await using var db = TestHarness.NewContext(nameof(A_graduation_named_in_the_file_is_still_recorded));
        db.SeedCatalog();
        db.SeedLevel(SixthYearLevelId, "6ème année", year: 6);

        var defended = db.SeedRegistration("Sara", "Bennani", levelId: SixthYearLevelId);
        var stillThere = db.SeedRegistration("Ali", "Amrani", levelId: SixthYearLevelId);
        defended.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        stillThere.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        // The defence roll is the document the faculty actually holds, and naming a student in it is
        // how a diplôme is pronounced now that silence no longer does it.
        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, defended, "Diplômé")], confirmed: 0),
            default);

        result.IsSuccess.Should().BeTrue();

        var stored = await db.Registrations.ToListAsync();
        stored.Single(r => r.Id == defended.Id).Status.Should().Be(RegistrationStatus.Graduated);
        stored.Single(r => r.Id == stillThere.Id).OutcomeSource.Should().BeNull();
    }

    [Fact]
    public async Task A_student_whose_text_is_unknown_in_a_year_that_could_be_his_last_is_left_alone()
    {
        await using var db = TestHarness.NewContext(
            nameof(A_student_whose_text_is_unknown_in_a_year_that_could_be_his_last_is_left_alone));
        db.SeedCatalog();
        db.SeedLevel(SixthYearLevelId, "6ème année", year: 6);

        var unstamped = db.SeedRegistration("Sara", "Bennani", levelId: SixthYearLevelId);
        var listed = db.SeedRegistration("Ali", "Amrani", levelId: SixthYearLevelId);
        listed.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        // No stamp, on a year one of the programme's texts ends at. This used to block the whole file;
        // it now needs no special case at all, because nobody in a possible final year is decided for.
        var report = await PreviewHandler(db).Handle(Preview([Row(2, listed, "Redoublant")]), default);

        report.Value.CanApply.Should().BeTrue();
        report.Value.FinalYearUndecidedCount.Should().Be(1);
        report.Value.DefaultedCount.Should().Be(0);
        (await db.Registrations.SingleAsync(r => r.Id == unstamped.Id)).OutcomeSource.Should().BeNull();
    }

    [Fact]
    public async Task A_student_who_is_below_every_texts_final_year_needs_no_stamp()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_who_is_below_every_texts_final_year_needs_no_stamp));
        db.SeedCatalog();
        var listed = db.SeedRegistration("Ali", "Amrani");
        var unstamped = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        // 3rd year: whichever text applies, six or seven, it is not the last one. Nothing to decide.
        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, listed, "Redoublant")], confirmed: 1), default);

        result.IsSuccess.Should().BeTrue();
        (await db.Registrations.SingleAsync(r => r.Id == unstamped.Id))
            .Status.Should().Be(RegistrationStatus.Validated);
    }

    [Fact]
    public async Task The_default_never_overwrites_a_verdict_already_recorded()
    {
        await using var db = TestHarness.NewContext(nameof(The_default_never_overwrites_a_verdict_already_recorded));
        db.SeedCatalog();

        var corrected = db.SeedRegistration("Sara", "Bennani");
        var listed = db.SeedRegistration("Ali", "Amrani");
        corrected.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        // Re-uploading last week's exceptions file must not undo the twelve verdicts corrected by hand
        // since — which is exactly what "everyone not named is admis" would do if it were unconditional.
        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, listed, "Redoublant")], confirmed: 0), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyDecidedCount.Should().Be(1);
        result.Value.DefaultedCount.Should().Be(0);

        (await db.Registrations.SingleAsync(r => r.Id == corrected.Id))
            .Status.Should().Be(RegistrationStatus.Failed);
    }

    [Fact]
    public async Task A_marker_that_is_not_a_year_of_study_is_never_promoted()
    {
        await using var db = TestHarness.NewContext(nameof(A_marker_that_is_not_a_year_of_study_is_never_promoted));
        db.SeedCatalog();

        // « Retrait » — CODE_N = 'MED00', a status the Access base wore as a level. Year 0, no stage,
        // nobody to promote, and it is offered wherever a promotion is.
        db.SeedLevel(levelId: 99, "Retrait", year: 0);
        var withdrawn = db.SeedRegistration("Sara", "Bennani", levelId: 99);
        var listed = db.SeedRegistration("Ali", "Amrani");
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, listed, "Redoublant")], confirmed: 0), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.NotAPromotionCount.Should().Be(1);
        (await db.Registrations.SingleAsync(r => r.Id == withdrawn.Id)).OutcomeSource.Should().BeNull();
    }

    [Fact]
    public async Task One_file_closes_every_promotion_of_the_year_each_at_its_own_level()
    {
        await using var db = TestHarness.NewContext(nameof(One_file_closes_every_promotion_of_the_year_each_at_its_own_level));
        db.SeedCatalog();
        db.SeedLevel(levelId: 4, "4ème année", year: 4);

        var third = db.SeedRegistration("Sara", "Bennani");
        var fourth = db.SeedRegistration("Ali", "Amrani", levelId: 4);
        var listed = db.SeedRegistration("Yasmine", "Idrissi", levelId: 4);
        await db.SaveChangesAsync();

        var report = await PreviewHandler(db).Handle(Preview([Row(2, listed, "Exclu")]), default);

        report.Value.ScopeLabel.Should().Be("Toutes les promotions");
        report.Value.DefaultedCount.Should().Be(2);

        // The breakdown is what the confirmation is read from: a total of 641 says nothing about
        // which promotion is about to be closed by silence.
        report.Value.ByLevel.Should().HaveCount(2);
        report.Value.ByLevel.Single(b => b.LevelLabel == "3ème année").WillPromote.Should().Be(1);
        report.Value.ByLevel.Single(b => b.LevelLabel == "4ème année").Listed.Should().Be(1);

        var result = await ApplyHandler(db).Handle(Apply([Row(2, listed, "Exclu")], confirmed: 2), default);
        result.IsSuccess.Should().BeTrue();

        var stored = await db.Registrations.ToListAsync();
        stored.Single(r => r.Id == third.Id).Status.Should().Be(RegistrationStatus.Validated);
        stored.Single(r => r.Id == fourth.Id).Status.Should().Be(RegistrationStatus.Validated);
        stored.Single(r => r.Id == listed.Id).Status.Should().Be(RegistrationStatus.Excluded);
    }

    [Fact]
    public async Task Applying_without_confirming_how_many_are_admitted_by_silence_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Applying_without_confirming_how_many_are_admitted_by_silence_is_refused));
        db.SeedCatalog();
        var listed = db.SeedRegistration("Ali", "Amrani");
        db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, listed, "Redoublant")], confirmed: null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliberation.DefaultsNotConfirmed");
        (await db.Registrations.CountAsync(r => r.OutcomeSource != null)).Should().Be(0);
    }

    [Fact]
    public async Task A_student_registered_between_the_simulation_and_the_apply_refuses_the_apply()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_registered_between_the_simulation_and_the_apply_refuses_the_apply));
        db.SeedCatalog();
        var listed = db.SeedRegistration("Ali", "Amrani");
        db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var preview = await PreviewHandler(db).Handle(Preview([Row(2, listed, "Redoublant")]), default);
        preview.Value.DefaultedCount.Should().Be(1);

        // A late registration is exactly the case a checkbox waves through: the operator confirmed one
        // student admitted by silence, and there are now two.
        db.SeedRegistration("Yasmine", "Idrissi");
        await db.SaveChangesAsync();

        var result = await ApplyHandler(db).Handle(
            Apply([Row(2, listed, "Redoublant")], confirmed: preview.Value.DefaultedCount), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Deliberation.DefaultsNotConfirmed");
        (await db.Registrations.CountAsync(r => r.OutcomeSource != null)).Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_identifier_still_refuses_the_whole_file_under_the_default()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_identifier_still_refuses_the_whole_file_under_the_default));
        db.SeedCatalog();
        db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        // The dangerous shape: a mistyped CNE means the student it was meant for is admitted by
        // silence. All-or-nothing is what stops that, and it has to survive the exceptions mode.
        var result = await ApplyHandler(db).Handle(
            Apply([new DeliberationRow(2, "CNE-QUI-NEXISTE-PAS", null, "Redoublant", null)], confirmed: 1),
            default);

        result.IsFailure.Should().BeTrue();
        (await db.Registrations.CountAsync(r => r.OutcomeSource != null)).Should().Be(0);
    }
}
