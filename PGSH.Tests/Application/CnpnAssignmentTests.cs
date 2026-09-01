using FluentAssertions;
using PGSH.Application.Stages.Cnpn;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Which CNPN governs a student. Arrêté 1650.25 art. 2 assigns by <i>date of first registration</i> —
/// students registered before 2024-2025 stay under the previous text — and the assignment then
/// follows them to graduation however long they take.
///
/// The cases that matter are the ones where the decree's criterion parts company with the intuitive
/// "which year are they in now": a repeater sits in an early level while belonging to an old text,
/// and a student whose entry was never imported has to be placed by deduction.
/// </summary>
public class CnpnAssignmentTests
{
    private const int OldText = TestHarness.OldCnpnId;
    private const int NewText = TestHarness.NewCnpnId;
    private const int Year2023 = 11, Year2024 = 12;

    /// <summary>
    /// Three academic years and two texts: the seven-year one governing entrants from 2023-2024, the
    /// six-year one from 2024-2025. Mirrors the real transition.
    /// </summary>
    private static void SeedTexts(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedAcademicYear(Year2023, "2023-2024", new DateOnly(2023, 9, 1), new DateOnly(2024, 8, 31));
        db.SeedAcademicYear(Year2024, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        // SeedCatalog's pair is replaced: these need real intake years to select between.
        db.CnpnVersions.Remove(db.CnpnVersions.Local.First(v => v.Id == OldText));
        db.CnpnVersions.Remove(db.CnpnVersions.Local.First(v => v.Id == NewText));
        db.SeedCnpnVersion(OldText, "2174.18", totalYears: 7, appliesFromAcademicYearId: Year2023);
        db.SeedCnpnVersion(NewText, "1650.25", totalYears: 6, appliesFromAcademicYearId: Year2024);
    }

    /// <summary>
    /// The three years above, ordered by start date — what <see cref="EntryYearDeduction"/> walks.
    /// </summary>
    private static readonly EntryYearDeduction.AcademicYearRef[] Years =
    [
        new(Year2023, new DateOnly(2023, 9, 1)),
        new(Year2024, new DateOnly(2024, 9, 1)),
        new(TestHarness.CurrentYearId, new DateOnly(2025, 9, 1)),
    ];

    // -- Which text governs an intake -----------------------------------------

    [Fact]
    public async Task An_entrant_of_the_year_the_new_text_took_effect_is_governed_by_it()
    {
        await using var db = TestHarness.NewContext("cnpn-new-entrant");
        SeedTexts(db);
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .SelectVersionAsync(AcademicProgram.Medecine, Year2024, default);

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Should().Be(NewText);
    }

    [Fact]
    public async Task An_entrant_of_the_year_before_stays_under_the_previous_text()
    {
        await using var db = TestHarness.NewContext("cnpn-old-entrant");
        SeedTexts(db);
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .SelectVersionAsync(AcademicProgram.Medecine, Year2023, default);

        resolved.Value.Should().Be(OldText);
    }

    [Fact]
    public async Task A_text_kept_only_for_the_record_never_governs_an_intake()
    {
        // Arrêté 2175.22 amended the 2019 text and was then explicitly disapplied by 1650.25, which
        // sends pre-2024-2025 students back to the pre-amendment form. It must be recorded and never
        // selected.
        await using var db = TestHarness.NewContext("cnpn-history-only");
        SeedTexts(db);
        db.SeedCnpnVersion(93, "2175.22", totalYears: 7, appliesFromAcademicYearId: null);
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .SelectVersionAsync(AcademicProgram.Medecine, Year2023, default);

        resolved.Value.Should().Be(OldText);
    }

    [Fact]
    public async Task An_intake_older_than_every_recorded_text_is_refused_rather_than_guessed()
    {
        await using var db = TestHarness.NewContext("cnpn-too-old");
        SeedTexts(db);
        db.SeedAcademicYear(5, "2015-2016", new DateOnly(2015, 9, 1), new DateOnly(2016, 8, 31));
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .SelectVersionAsync(AcademicProgram.Medecine, 5, default);

        resolved.IsFailure.Should().BeTrue("guessing here would shorten someone's degree");
        resolved.Error.Code.Should().Be("Cnpn.NoVersionForIntake");
    }

    [Fact]
    public async Task An_intake_year_that_does_not_exist_is_refused()
    {
        await using var db = TestHarness.NewContext("cnpn-unknown-intake");
        SeedTexts(db);
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .SelectVersionAsync(AcademicProgram.Medecine, 4242, default);

        resolved.IsFailure.Should().BeTrue();
        resolved.Error.Code.Should().Be("AcademicYears.NotFound");
    }

    // -- When the student entered ---------------------------------------------
    //
    // The deduction is pure, so the cases are exact rather than approximately seeded. It carries the
    // one assumption the whole backfill rests on: ~2,200 enrolled students first appear in the data
    // at level 2 or above, because the legacy import only carried them once they had stages.

    [Fact]
    public void A_first_registration_at_the_first_level_is_the_entry_itself()
    {
        EntryYearDeduction.IsRecordedEntry(1).Should().BeTrue();

        EntryYearDeduction.EntryYearId(Years, Year2024, levelYearAtEarliestRegistration: 1)
            .Should().Be(Year2024);
    }

    [Fact]
    public void An_unrecorded_entry_is_walked_back_one_year_per_level_and_flagged_as_deduced()
    {
        EntryYearDeduction.IsRecordedEntry(3).Should().BeFalse(
            "you cannot be in the third year without having spent two — the answer is offered as "
            + "inference, not as fact");

        EntryYearDeduction.EntryYearId(Years, TestHarness.CurrentYearId, 3)
            .Should().Be(Year2023, "third year in 2025-2026 means an entry two years earlier");
    }

    [Fact]
    public void The_walk_back_stops_at_the_earliest_year_on_record()
    {
        // History does not reach far enough, which still lands before any modern CNPN: the answer
        // stays right even when the exact year does not.
        EntryYearDeduction.EntryYearId(Years, Year2023, levelYearAtEarliestRegistration: 7)
            .Should().Be(Year2023);
    }

    [Fact]
    public void A_year_that_cannot_be_placed_is_returned_unchanged()
    {
        EntryYearDeduction.EntryYearId(Years, 4242, levelYearAtEarliestRegistration: 3)
            .Should().Be(4242, "a year we cannot place is a year we cannot walk back from");
    }

    [Fact]
    public async Task A_repeater_sitting_in_an_early_level_keeps_the_text_of_their_entry()
    {
        // The case the decree and the "which level are they in now" shortcut disagree on: entered
        // 2023-2024, failed the first year, and is still in an early level today. Twenty-one real
        // students are in exactly this position.
        await using var db = TestHarness.NewContext("cnpn-repeater");
        SeedTexts(db);
        await db.SaveChangesAsync();

        int entryYearId = EntryYearDeduction.EntryYearId(
            Years, earliestKnownYearId: Year2023, levelYearAtEarliestRegistration: 1);

        var resolved = await new CnpnAssignment(db)
            .SelectVersionAsync(AcademicProgram.Medecine, entryYearId, default);

        resolved.Value.Should().Be(OldText,
            "the text follows the intake, not the level the student happens to sit in");
    }

    [Fact]
    public async Task A_deduced_entry_places_the_student_under_the_text_of_that_year()
    {
        await using var db = TestHarness.NewContext("cnpn-deduced");
        SeedTexts(db);
        await db.SaveChangesAsync();

        int entryYearId = EntryYearDeduction.EntryYearId(
            Years, earliestKnownYearId: TestHarness.CurrentYearId, levelYearAtEarliestRegistration: 3);

        var resolved = await new CnpnAssignment(db)
            .SelectVersionAsync(AcademicProgram.Medecine, entryYearId, default);

        resolved.Value.Should().Be(OldText,
            "third year in 2025-2026 means an entry two years earlier, before the new text");
    }

    // A student with no registration at all is no longer this class's case: the text is resolved as
    // a registration is created, so the registration being created is its own entry evidence.
    // "unresolvable -> created without a text rather than refused" is covered by
    // CnpnEffectivityTests.An_unresolvable_registration_is_created_without_a_text_rather_than_refused.

    // ── The stamp itself ─────────────────────────────────────────────────────

    [Fact]
    public void A_confirmed_assignment_is_not_moved_by_a_re_run()
    {
        var student = NewStudent();
        student.AssignCnpnVersion(OldText, isInferred: false).IsSuccess.Should().BeTrue();

        var second = student.AssignCnpnVersion(NewText, isInferred: false);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("Students.CnpnAlreadyAssigned");
        student.CnpnVersionId.Should().Be(OldText);
    }

    [Fact]
    public void An_inferred_assignment_can_be_upgraded_to_a_confirmed_one()
    {
        var student = NewStudent();
        student.AssignCnpnVersion(OldText, isInferred: true);

        student.AssignCnpnVersion(NewText, isInferred: false).IsSuccess.Should().BeTrue();

        student.CnpnVersionId.Should().Be(NewText);
        student.CnpnAssignmentIsInferred.Should().BeFalse();
    }

    [Fact]
    public void Re_stamping_the_same_confirmed_text_is_a_no_op()
    {
        var student = NewStudent();
        student.AssignCnpnVersion(OldText, isInferred: false);
        student.ClearDomainEvents();

        student.AssignCnpnVersion(OldText, isInferred: true).IsSuccess.Should().BeTrue();

        student.CnpnAssignmentIsInferred.Should().BeFalse("a confirmed reading never regresses");
        student.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_deliberate_correction_can_override_a_confirmed_assignment()
    {
        var student = NewStudent();
        student.AssignCnpnVersion(OldText, isInferred: false);

        student.AssignCnpnVersion(NewText, isInferred: false, overrideExisting: true)
            .IsSuccess.Should().BeTrue();

        student.CnpnVersionId.Should().Be(NewText);
        student.DomainEvents.OfType<StudentCnpnVersionAssignedDomainEvent>()
            .Should().Contain(e => e.PreviousCnpnVersionId == OldText && e.NewCnpnVersionId == NewText);
    }

    private static Student NewStudent() => new()
    {
        Id = Guid.NewGuid(), FirstName = "Test", LastName = "Étudiant",
        Email = "t@etu.ma", CNE = "CNE111111", Appogee = "AP111111", BacYear = "2024",
    };
}
