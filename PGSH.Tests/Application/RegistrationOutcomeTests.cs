using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Students.Registrations.Outcome;
using PGSH.Application.Students.Registrations.Update;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// One registration's verdict, recorded and withdrawn without a file. The canvas closes a promotion;
/// a late jury, a corrected PV or an abandon notified in November closes one student — and under an
/// exceptions file that path has to exist, because re-uploading the promotion's file is precisely what
/// must not be needed to fix one row.
/// </summary>
public class RegistrationOutcomeTests
{
    private const int SixthYearLevelId = 6;
    private const int NextYearId = 3;

    private static RecordRegistrationOutcomeCommandHandler RecordHandler(ApplicationDbContext db) =>
        new(db, db.AdminAuthorizer());

    private static ReopenRegistrationYearCommandHandler ReopenHandler(ApplicationDbContext db) =>
        new(db, db.AdminAuthorizer());

    [Fact]
    public async Task Recording_one_verdict_stamps_it_declared_like_the_canvas_does()
    {
        await using var db = TestHarness.NewContext(nameof(Recording_one_verdict_stamps_it_declared_like_the_canvas_does));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await RecordHandler(db).Handle(
            new RecordRegistrationOutcomeCommand(
                registration.Id, RegistrationStatus.Failed, "Stage non validé"),
            default);

        result.IsSuccess.Should().BeTrue();

        var stored = await db.Registrations.SingleAsync(r => r.Id == registration.Id);
        stored.Status.Should().Be(RegistrationStatus.Failed);
        stored.OutcomeSource.Should().Be(RegistrationOutcomeSource.Declared);
        stored.failureReasons!.Description.Should().Be("Stage non validé");
    }

    [Fact]
    public async Task A_motif_on_a_favourable_verdict_is_dropped_exactly_as_the_canvas_drops_it()
    {
        await using var db = TestHarness.NewContext(nameof(A_motif_on_a_favourable_verdict_is_dropped_exactly_as_the_canvas_drops_it));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        await RecordHandler(db).Handle(
            new RecordRegistrationOutcomeCommand(registration.Id, RegistrationStatus.Validated, "rattrapage"),
            default);

        (await db.Registrations.SingleAsync(r => r.Id == registration.Id))
            .failureReasons.Should().BeNull();
    }

    [Fact]
    public async Task Diplome_off_the_last_year_of_the_students_text_is_refused_for_one_student_too()
    {
        await using var db = TestHarness.NewContext(nameof(Diplome_off_the_last_year_of_the_students_text_is_refused_for_one_student_too));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");

        // SeedCatalog's level is the 3rd year; the text in force runs six.
        registration.Student!.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false);
        await db.SaveChangesAsync();

        var result = await RecordHandler(db).Handle(
            new RecordRegistrationOutcomeCommand(registration.Id, RegistrationStatus.Graduated),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.NotAFinalYear");
        (await db.Registrations.SingleAsync(r => r.Id == registration.Id)).OutcomeSource.Should().BeNull();
    }

    [Fact]
    public async Task Diplome_stands_aside_where_the_student_carries_no_text()
    {
        await using var db = TestHarness.NewContext(nameof(Diplome_stands_aside_where_the_student_carries_no_text));
        db.SeedCatalog();
        db.SeedLevel(SixthYearLevelId, "6ème année", year: 6);
        var registration = db.SeedRegistration("Sara", "Bennani", levelId: SixthYearLevelId);
        await db.SaveChangesAsync();

        // One student at a time must not be stricter than five hundred at once — the canvas stands
        // aside on an absent stamp, and 19 students in the base carry none.
        var result = await RecordHandler(db).Handle(
            new RecordRegistrationOutcomeCommand(registration.Id, RegistrationStatus.Graduated),
            default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_status_that_is_not_a_verdict_cannot_be_recorded_as_one()
    {
        await using var db = TestHarness.NewContext(nameof(A_status_that_is_not_a_verdict_cannot_be_recorded_as_one));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await RecordHandler(db).Handle(
            new RecordRegistrationOutcomeCommand(registration.Id, RegistrationStatus.Active),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.NotAYearOutcome");
    }

    [Fact]
    public async Task Reopening_withdraws_the_verdict_and_says_the_next_year_already_exists()
    {
        await using var db = TestHarness.NewContext(nameof(Reopening_withdraws_the_verdict_and_says_the_next_year_already_exists));
        db.SeedCatalog();
        db.SeedAcademicYear(NextYearId, "2026-2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));

        var registration = db.SeedRegistration("Sara", "Bennani");
        registration.RecordYearOutcome(
            RegistrationStatus.Validated, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);

        // The rollover already ran on the verdict now being withdrawn.
        var next = db.SeedRegistration("Sara", "Bennani", academicYearId: NextYearId);
        next.StudentId = registration.StudentId;
        next.Student = registration.Student!;
        await db.SaveChangesAsync();

        var result = await ReopenHandler(db).Handle(
            new ReopenRegistrationYearCommand(registration.Id, "PV erroné"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WithdrawnOutcome.Should().Be(RegistrationStatus.Validated);

        // ⚠ Reported, never removed: that row may already carry a group, cohorts and published
        // périodes, and cascading a correction into it would delete a student's rotations.
        result.Value.LaterRegistrationExists.Should().BeTrue();
        (await db.Registrations.CountAsync(r => r.AcademicYearId == NextYearId)).Should().Be(1);

        var reopened = await db.Registrations.SingleAsync(r => r.Id == registration.Id);
        reopened.Status.Should().Be(RegistrationStatus.Active);
        reopened.OutcomeSource.Should().BeNull();
        reopened.OutcomeRecordedOn.Should().BeNull();
    }

    [Fact]
    public async Task Reopening_a_year_nobody_closed_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Reopening_a_year_nobody_closed_is_refused));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await ReopenHandler(db).Handle(
            new ReopenRegistrationYearCommand(registration.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.NoOutcomeToReopen");
    }

    [Fact]
    public async Task Only_the_scolarite_can_close_or_reopen_one_students_year()
    {
        await using var db = TestHarness.NewContext(nameof(Only_the_scolarite_can_close_or_reopen_one_students_year));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await new RecordRegistrationOutcomeCommandHandler(db, db.StrangerAuthorizer()).Handle(
            new RecordRegistrationOutcomeCommand(registration.Id, RegistrationStatus.Validated),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Registrations.OutcomeNotAllowed");
        (await db.Registrations.SingleAsync(r => r.Id == registration.Id)).OutcomeSource.Should().BeNull();
    }

    [Fact]
    public async Task Editing_a_registration_to_a_verdict_records_it_rather_than_assigning_the_field()
    {
        await using var db = TestHarness.NewContext(nameof(Editing_a_registration_to_a_verdict_records_it_rather_than_assigning_the_field));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");
        registration.Student!.AcademicProgram = AcademicProgram.Medecine;
        await db.SaveChangesAsync();

        // ⚠ The edit form used to write Status directly, leaving OutcomeSource null: the screen showed
        // « Admis » while the réinscription reported « aucune décision enregistrée » and refused to
        // carry the student over. Neither was wrong about what it read.
        var result = await new UpdateRegistrationCommandHandler(db).Handle(
            new UpdateRegistrationCommand(
                registration.Id, registration.StudentId, RegistrationStatus.Validated,
                TestHarness.CurrentYearId, TestHarness.LevelId),
            default);

        result.IsSuccess.Should().BeTrue();

        var stored = await db.Registrations.SingleAsync(r => r.Id == registration.Id);
        stored.Status.Should().Be(RegistrationStatus.Validated);
        stored.OutcomeSource.Should().Be(RegistrationOutcomeSource.Declared);
        stored.OutcomeRecordedOn.Should().NotBeNull();
    }

    [Fact]
    public async Task Editing_a_closed_registration_back_to_active_withdraws_the_verdict()
    {
        await using var db = TestHarness.NewContext(nameof(Editing_a_closed_registration_back_to_active_withdraws_the_verdict));
        db.SeedCatalog();
        var registration = db.SeedRegistration("Sara", "Bennani");
        registration.Student!.AcademicProgram = AcademicProgram.Medecine;
        registration.RecordYearOutcome(
            RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var result = await new UpdateRegistrationCommandHandler(db).Handle(
            new UpdateRegistrationCommand(
                registration.Id, registration.StudentId, RegistrationStatus.Active,
                TestHarness.CurrentYearId, TestHarness.LevelId),
            default);

        result.IsSuccess.Should().BeTrue();

        var stored = await db.Registrations.SingleAsync(r => r.Id == registration.Id);
        stored.Status.Should().Be(RegistrationStatus.Active);
        stored.OutcomeSource.Should().BeNull();
    }
}
