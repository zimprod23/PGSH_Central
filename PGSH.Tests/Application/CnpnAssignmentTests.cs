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

    private static int LevelFor(ApplicationDbContext db, int year)
    {
        var level = new Level
        {
            Id = 50 + year, Label = $"{year}e année", Year = year,
            AcademicProgram = AcademicProgram.Medecine,
        };
        db.Levels.Add(level);
        return level.Id;
    }

    [Fact]
    public async Task An_entrant_of_the_year_the_new_text_took_effect_is_governed_by_it()
    {
        await using var db = TestHarness.NewContext("cnpn-new-entrant");
        SeedTexts(db);
        var reg = db.SeedRegistration("Sara", "Bennani", null, Year2024, LevelFor(db, 1));
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .ResolveAsync(reg.StudentId, TestHarness.CurrentYearId, default);

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.CnpnVersionId.Should().Be(NewText);
        resolved.Value.IsInferred.Should().BeFalse();
    }

    [Fact]
    public async Task An_entrant_of_the_year_before_stays_under_the_previous_text()
    {
        await using var db = TestHarness.NewContext("cnpn-old-entrant");
        SeedTexts(db);
        var reg = db.SeedRegistration("Ali", "Amrani", null, Year2023, LevelFor(db, 1));
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .ResolveAsync(reg.StudentId, TestHarness.CurrentYearId, default);

        resolved.Value.CnpnVersionId.Should().Be(OldText);
        resolved.Value.IsInferred.Should().BeFalse();
    }

    [Fact]
    public async Task A_repeater_sitting_in_an_early_level_keeps_the_text_of_their_entry()
    {
        // The case the decree and the "which level are they in now" shortcut disagree on: entered
        // 2023-2024, failed the first year, and is still in an early level today. Twenty-one real
        // students are in exactly this position.
        await using var db = TestHarness.NewContext("cnpn-repeater");
        SeedTexts(db);

        int firstYear = LevelFor(db, 1);
        var reg = db.SeedRegistration("Nadia", "Idrissi", null, Year2023, firstYear);
        db.Registrations.Add(new PGSH.Domain.Registrations.Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = Year2024, LevelId = firstYear,
            StudentId = reg.StudentId, Student = reg.Student,
        });
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .ResolveAsync(reg.StudentId, TestHarness.CurrentYearId, default);

        resolved.Value.CnpnVersionId.Should().Be(OldText,
            "the text follows the intake, not the level the student happens to sit in");
        resolved.Value.IsInferred.Should().BeFalse();
    }

    [Fact]
    public async Task An_unrecorded_entry_is_deduced_from_the_level_and_flagged()
    {
        // ~2,200 enrolled students first appear in the data at level 2 or above: the legacy import
        // only carried them once they had stages. You cannot be in the third year without having
        // spent two, so entry is deduced — and the answer is offered as inference, not as fact.
        await using var db = TestHarness.NewContext("cnpn-deduced");
        SeedTexts(db);
        var reg = db.SeedRegistration("Omar", "Tazi", null, TestHarness.CurrentYearId, LevelFor(db, 3));
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .ResolveAsync(reg.StudentId, TestHarness.CurrentYearId, default);

        resolved.Value.IsInferred.Should().BeTrue();
        resolved.Value.CnpnVersionId.Should().Be(OldText,
            "third year in 2025-2026 means an entry two years earlier, before the new text");
    }

    [Fact]
    public async Task A_student_with_no_registration_cannot_be_placed()
    {
        await using var db = TestHarness.NewContext("cnpn-no-registration");
        SeedTexts(db);
        var orphan = new Student
        {
            Id = Guid.NewGuid(), FirstName = "Sans", LastName = "Inscription",
            Email = "sans@etu.ma", CNE = "CNE000000", Appogee = "AP000000", BacYear = "2022",
        };
        db.Users.Add(orphan);
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .ResolveAsync(orphan.Id, TestHarness.CurrentYearId, default);

        resolved.IsFailure.Should().BeTrue();
        resolved.Error.Code.Should().Be("Cnpn.NoRegistration");
    }

    [Fact]
    public async Task A_text_kept_only_for_the_record_never_governs_an_intake()
    {
        // Arrêté 2175.22 amended the 2019 text and was then explicitly disapplied by 1650.25, which
        // sends pre-2024-2025 students back to the pre-amendment form. It must resolve as a citation
        // and never be selected.
        await using var db = TestHarness.NewContext("cnpn-history-only");
        SeedTexts(db);
        db.SeedCnpnVersion(93, "2175.22", totalYears: 7, appliesFromAcademicYearId: null);
        var reg = db.SeedRegistration("Yassine", "Alami", null, Year2023, LevelFor(db, 1));
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .ResolveAsync(reg.StudentId, TestHarness.CurrentYearId, default);

        resolved.Value.CnpnVersionId.Should().Be(OldText);
    }

    [Fact]
    public async Task An_intake_older_than_every_recorded_text_is_refused_rather_than_guessed()
    {
        await using var db = TestHarness.NewContext("cnpn-too-old");
        SeedTexts(db);
        db.SeedAcademicYear(5, "2015-2016", new DateOnly(2015, 9, 1), new DateOnly(2016, 8, 31));
        var reg = db.SeedRegistration("Ancien", "Étudiant", null, 5, LevelFor(db, 1));
        await db.SaveChangesAsync();

        var resolved = await new CnpnAssignment(db)
            .ResolveAsync(reg.StudentId, TestHarness.CurrentYearId, default);

        resolved.IsFailure.Should().BeTrue("guessing here would shorten someone's degree");
        resolved.Error.Code.Should().Be("Cnpn.NoVersionForIntake");
    }

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
