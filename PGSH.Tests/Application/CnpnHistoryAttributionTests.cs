using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Cnpn.SeedFromHistory;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The pass that gives an imported base its CNPN stamps.
///
/// <para>⚠ <b>What it exists for is a gap nothing else can close.</b> The student attribution was a
/// single <c>UPDATE</c> inside the <c>CnpnVersioning</c> migration and the registration backfill
/// another inside <c>RegistrationCnpnAndLevelEffectivity</c>. Both were written to run over data that
/// was already there; replayed against a database rebuilt from the .mdb they run before the import
/// and stamp nobody, and are then marked applied so nothing runs them again. The result is a base
/// where every student and every registration carries a null text — which every reader tolerates
/// gracefully, so nothing complains, and the déliberation quietly stops knowing whose year might be
/// his last.</para>
/// </summary>
public class CnpnHistoryAttributionTests
{
    private const int Year2023 = 30;
    private const int Year2024 = 31;
    private const int FirstYearLevelId = 40;
    private const int OldTextId = 80;

    private static CnpnHistoryAttributor Attributor(ApplicationDbContext db) =>
        new(db, new CnpnAssignment(db));

    /// <summary>
    /// Three consecutive years so entry can actually be walked back to, a first-year level so a
    /// recorded entry is expressible, and a second text governing the older intake.
    /// </summary>
    private static void SeedYears(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedAcademicYear(Year2023, "2023-2024", new DateOnly(2023, 9, 1), new DateOnly(2024, 8, 31));
        db.SeedAcademicYear(Year2024, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedLevel(FirstYearLevelId, "1ère année", year: 1);

        // In force for the 2023-2024 intake; TestHarness's 1650.25 takes over from 2025-2026.
        db.SeedCnpnVersion(OldTextId, "2174.18-bis", totalYears: 7, appliesFromAcademicYearId: Year2023);
    }

    private static Registration Enrolled(
        ApplicationDbContext db, string last, int yearId, int levelId) =>
        db.SeedRegistration("Amine", last, academicYearId: yearId, levelId: levelId);

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A first registration at level 1 <em>is</em> the entry, so the stamp is not inferred and the
    /// text is the one in force that year.
    /// </summary>
    [Fact]
    public async Task A_recorded_entry_is_stamped_from_its_own_year_and_not_flagged_inferred()
    {
        await using var db = TestHarness.NewContext(nameof(A_recorded_entry_is_stamped_from_its_own_year_and_not_flagged_inferred));
        SeedYears(db);
        var registration = Enrolled(db, "Bennani", Year2023, FirstYearLevelId);
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.IsSuccess.Should().BeTrue();
        report.Value.StudentsStamped.Should().Be(1);
        report.Value.StudentsInferred.Should().Be(0);

        registration.Student.CnpnVersionId.Should().Be(OldTextId);
        registration.Student.CnpnAssignmentIsInferred.Should().BeFalse();
    }

    /// <summary>
    /// ⚠ The assumption the whole backfill rests on: « on ne peut pas être en 3ᵉ année sans avoir
    /// passé deux ans ». The legacy base only carried a student once he had stages, so ~2 200
    /// enrolled students have no registration before 2025-2026 even though they plainly did not start
    /// there. Entry is walked back <c>level - 1</c> years, and the stamp says it was deduced.
    /// </summary>
    [Fact]
    public async Task An_entry_above_the_first_year_is_walked_back_and_flagged_inferred()
    {
        await using var db = TestHarness.NewContext(nameof(An_entry_above_the_first_year_is_walked_back_and_flagged_inferred));
        SeedYears(db);

        // 3ᵉ année in 2025-2026 → entry two years earlier, i.e. 2023-2024, i.e. the older text.
        var registration = Enrolled(db, "Alaoui", TestHarness.CurrentYearId, TestHarness.LevelId);
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.Value.StudentsStamped.Should().Be(1);
        report.Value.StudentsInferred.Should().Be(1);

        registration.Student.CnpnVersionId.Should().Be(OldTextId,
            "the text is the one governing his deduced entry, not the one in force where he sits");
        registration.Student.CnpnAssignmentIsInferred.Should().BeTrue(
            "surfaced for scolarité, never presented as fact");
    }

    /// <summary>
    /// ⚠ A confirmed stamp is never moved. The pass exists to fill a blank base; re-run over one
    /// where scolarité has confirmed assignments it must leave them exactly as they are.
    /// </summary>
    [Fact]
    public async Task A_confirmed_stamp_is_left_alone()
    {
        await using var db = TestHarness.NewContext(nameof(A_confirmed_stamp_is_left_alone));
        SeedYears(db);
        var registration = Enrolled(db, "Chraibi", TestHarness.CurrentYearId, TestHarness.LevelId);
        registration.Student.AssignCnpnVersion(TestHarness.NewCnpnId, isInferred: false)
            .IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.Value.StudentsStamped.Should().Be(0);
        report.Value.StudentsAlreadySettled.Should().Be(1);
        registration.Student.CnpnVersionId.Should().Be(TestHarness.NewCnpnId);
    }

    /// <summary>
    /// Every registration gets the student's text where it has none, marked <c>Backfilled</c> —
    /// deliberately not <c>StudentStamp</c>, because nobody was asked at the time.
    /// </summary>
    [Fact]
    public async Task Registrations_are_backfilled_from_the_student_stamp()
    {
        await using var db = TestHarness.NewContext(nameof(Registrations_are_backfilled_from_the_student_stamp));
        SeedYears(db);

        var first = Enrolled(db, "Tazi", Year2023, FirstYearLevelId);
        var second = new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = Year2024, LevelId = FirstYearLevelId,
            StudentId = first.StudentId, Student = first.Student,
        };
        db.Registrations.Add(second);
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.Value.RegistrationsBackfilled.Should().Be(2);
        first.CnpnVersionId.Should().Be(OldTextId);
        first.CnpnSource.Should().Be(RegistrationCnpnSource.Backfilled);
        second.CnpnVersionId.Should().Be(OldTextId);
    }

    /// <summary>
    /// A registration that already names a text keeps it: <c>Registration.CnpnVersionId</c> is what
    /// the student owed <em>that</em> year, and it is not the student's current stamp restated.
    /// </summary>
    [Fact]
    public async Task A_registration_that_already_names_a_text_is_not_rewritten()
    {
        await using var db = TestHarness.NewContext(nameof(A_registration_that_already_names_a_text_is_not_rewritten));
        SeedYears(db);
        var registration = Enrolled(db, "Idrissi", Year2023, FirstYearLevelId);
        registration.StampCnpnVersion(TestHarness.NewCnpnId, RegistrationCnpnSource.Effectivity)
            .IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.Value.RegistrationsBackfilled.Should().Be(0);
        registration.CnpnVersionId.Should().Be(TestHarness.NewCnpnId);
        registration.CnpnSource.Should().Be(RegistrationCnpnSource.Effectivity);
    }

    /// <summary>
    /// ⚠ <b>A pronounced year with no text on record is still backfilled, and the distinction is the
    /// point.</b> <c>Registration.CnpnFrozenByOutcome</c> refuses to <em>move</em> a stamp once a
    /// verdict has been recorded against it — a verdict whose obligations shifted afterwards is not
    /// readable. It does not refuse to record one where there was none: nothing moves, and the
    /// alternative is a closed year that can never say what it was judged against at all.
    ///
    /// <para>The case is not hypothetical the other way round either — <c>StampCnpnVersion</c> reads
    /// <c>CnpnVersionId is not null &amp;&amp; OutcomeSource is not null</c>, so the refusal is
    /// unreachable from this pass, which only loads the null ones. The <c>Result</c> is still checked
    /// and counted rather than discarded: the aggregate is the authority on its own invariant, and a
    /// pass that assumed its filter made a refusal impossible is how the next invariant gets ignored.</para>
    /// </summary>
    [Fact]
    public async Task A_pronounced_year_that_never_had_a_text_still_gets_one()
    {
        await using var db = TestHarness.NewContext(nameof(A_pronounced_year_that_never_had_a_text_still_gets_one));
        SeedYears(db);
        var registration = Enrolled(db, "Fassi", Year2023, FirstYearLevelId);
        registration.RecordYearOutcome(
            RegistrationStatus.Validated, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.Value.RegistrationsBackfilled.Should().Be(1);
        report.Value.RegistrationsRefusedByAggregate.Should().Be(0);
        registration.CnpnVersionId.Should().Be(OldTextId);
        registration.CnpnSource.Should().Be(RegistrationCnpnSource.Backfilled);
    }

    // ⚠ « A lone unplaceable student is reported, not refused » used to be asserted here, and it is
    // no longer true — nor should it be. With one student and nobody placed, the pass cannot tell
    // his intake from a catalogue holding no selectable text at all, which is exactly the state a
    // rebuild leaves behind. The distinction that matters is covered above, by the pair: a
    // population nobody can place refuses, one unplaceable student *beside a placeable one* is
    // reported. Keeping the old case would have cemented the blind spot as expected behaviour.

    [Fact]
    public async Task A_dry_run_reports_the_same_numbers_and_writes_nothing()
    {
        await using var db = TestHarness.NewContext(nameof(A_dry_run_reports_the_same_numbers_and_writes_nothing));
        SeedYears(db);
        Enrolled(db, "Bennani", Year2023, FirstYearLevelId);
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: true, default);

        report.Value.StudentsStamped.Should().Be(1);
        report.Value.RegistrationsBackfilled.Should().Be(1);
        report.Value.DryRun.Should().BeTrue();

        // Read back through a fresh query rather than the tracked graph, which the pass mutated.
        (await db.Registrations.AsNoTracking().CountAsync(r => r.CnpnVersionId != null)).Should().Be(0);
    }

    /// <summary>
    /// ⚠ <b>The guard that the 2026-09-01 rebuild needed and did not have.</b> Every
    /// <c>CnpnVersion</c> came out of the migration chain with a null intake year — because
    /// <c>CnpnVersioning</c> reads them from <c>AcademicYears</c>, which is empty when the migrations
    /// run before the import — and a text with no intake year is <em>citation-only</em>, a legitimate
    /// state. So nothing threw, nothing refused, and the pass reported <b>10 185 of 10 185 students
    /// unresolved</b> as an ordinary count and returned success.
    ///
    /// <para>One unplaceable student is a fact about him; the whole population is a broken catalogue,
    /// and the two must not read the same.</para>
    /// </summary>
    [Fact]
    public async Task A_catalogue_where_no_text_claims_an_intake_refuses_instead_of_stamping_nobody()
    {
        await using var db = TestHarness.NewContext(nameof(A_catalogue_where_no_text_claims_an_intake_refuses_instead_of_stamping_nobody));
        SeedYears(db);

        // Exactly what a rebuild leaves behind: every text recorded, none of them selectable.
        foreach (var version in db.CnpnVersions.Local.ToList())
            version.Correct(version.Code, version.Label, version.TotalYears, version.Reference,
                    appliesToEntrantsFromAcademicYearId: null, CnpnSpanFloor.None)
                .IsSuccess.Should().BeTrue();

        var registration = Enrolled(db, "Bennani", Year2023, FirstYearLevelId);
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.IsFailure.Should().BeTrue("a pass that places nobody has not succeeded");
        report.Error.Code.Should().Be("Cnpn.NoTextGovernsAnyIntake");

        // ⚠ And it refuses *before* the registrations are touched.
        registration.CnpnVersionId.Should().BeNull();
    }

    /// <summary>
    /// The control: one student nobody can place, beside one who can, is a fact about him — reported,
    /// never a refusal.
    /// </summary>
    [Fact]
    public async Task One_unplaceable_student_beside_a_placeable_one_is_only_reported()
    {
        await using var db = TestHarness.NewContext(nameof(One_unplaceable_student_beside_a_placeable_one_is_only_reported));
        SeedYears(db);
        db.SeedLevel(70, "1ère année Pharmacie", year: 1, program: AcademicProgram.Pharmacie);

        Enrolled(db, "Bennani", Year2023, FirstYearLevelId);
        var unplaceable = Enrolled(db, "Sqalli", Year2023, 70);
        await db.SaveChangesAsync();

        var report = await Attributor(db).AttributeAsync(dryRun: false, default);

        report.IsSuccess.Should().BeTrue();
        report.Value.StudentsStamped.Should().Be(1);
        report.Value.StudentsUnresolved.Should().Be(1);
        unplaceable.Student.CnpnVersionId.Should().BeNull();
    }

    /// <summary>
    /// Entry is deduced by walking a list of years, so a base holding none is a base the import has
    /// not run against yet — an error worth naming, not a pass that quietly stamps nobody.
    /// </summary>
    [Fact]
    public async Task A_base_with_no_academic_years_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(A_base_with_no_academic_years_is_refused));

        var report = await Attributor(db).AttributeAsync(dryRun: true, default);

        report.IsFailure.Should().BeTrue();
        report.Error.Code.Should().Be("Cnpn.NoAcademicYears");
    }
}
