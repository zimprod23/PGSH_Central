using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Cnpn.Targeting;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Rattacher une promotion à un CNPN: the faculty authors the rule ("Médecine, année ≤ N"), sees the
/// population, and only then freezes it.
///
/// Two properties matter more than the arithmetic. A confirmed stamp is never moved in bulk — that
/// would be precisely the way to defeat the per-student guard. And where the rule and the arrêté's own
/// wording disagree (the repeater sitting in an early level), the disagreement is *reported*, not
/// resolved: the system does not get to decide how many years someone owes.
/// </summary>
public class CnpnTargetingTests
{
    private const int OldText = TestHarness.OldCnpnId;
    private const int NewText = TestHarness.NewCnpnId;
    private const int Year2023 = 11, Year2024 = 12;

    /// <summary>
    /// Three years and two texts, mirroring the real transition: the seven-year text governs entrants
    /// from 2023-2024, the six-year one from 2024-2025 (the current year is 2025-2026).
    /// </summary>
    private static void SeedTexts(ApplicationDbContext db)
    {
        db.SeedCatalog();
        db.SeedAcademicYear(Year2023, "2023-2024", new DateOnly(2023, 9, 1), new DateOnly(2024, 8, 31));
        db.SeedAcademicYear(Year2024, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        db.CnpnVersions.Remove(db.CnpnVersions.Local.First(v => v.Id == OldText));
        db.CnpnVersions.Remove(db.CnpnVersions.Local.First(v => v.Id == NewText));
        db.SeedCnpnVersion(OldText, "2174.18", totalYears: 7, appliesFromAcademicYearId: Year2023);
        db.SeedCnpnVersion(NewText, "1650.25", totalYears: 6, appliesFromAcademicYearId: Year2024);
    }

    private static int Level(ApplicationDbContext db, int year,
        AcademicProgram program = AcademicProgram.Medecine)
    {
        int id = (int)program * 100 + year;
        if (db.Levels.Local.All(l => l.Id != id))
            db.Levels.Add(new Level
            {
                Id = id, Label = $"{year}e année {program}", Year = year, AcademicProgram = program,
            });
        return id;
    }

    /// <summary>
    /// One student carrying a registration in each of <paramref name="years"/>. The earliest is their
    /// entry, which is what the arrêté keys on — a repeater is exactly a student whose entry is older
    /// than the level they sit in suggests.
    /// </summary>
    private static Registration Enrol(
        ApplicationDbContext db, string last, int levelYear, params int[] years)
    {
        int levelId = Level(db, levelYear);
        var first = db.SeedRegistration("Test", last, null, years[0], levelId);

        foreach (int yearId in years.Skip(1))
        {
            db.Registrations.Add(new Registration
            {
                Id = Guid.NewGuid(),
                AcademicYearId = yearId,
                LevelId = levelId,
                StudentId = first.StudentId,
                Student = first.Student,
            });
        }

        return first;
    }

    private static CnpnTargetPlanner Planner(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static CnpnTargetCriteria Rule(int maxLevelYear, bool includeContradictions = false) =>
        new(AcademicProgram.Medecine, maxLevelYear, TestHarness.CurrentYearId, includeContradictions);

    // ── The happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_rule_catches_every_level_at_or_below_the_chosen_year()
    {
        await using var db = TestHarness.NewContext("target-cascade");
        SeedTexts(db);
        db.SeedRegistration("Un", "Première", null, TestHarness.CurrentYearId, Level(db, 1));
        db.SeedRegistration("Deux", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        db.SeedRegistration("Trois", "Troisième", null, TestHarness.CurrentYearId, Level(db, 3));
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(NewText, Rule(maxLevelYear: 2), default);

        plan.IsSuccess.Should().BeTrue();
        plan.Value.Preview.TotalMatched.Should().Be(2, "« et en dessous » stops at the chosen year");
        plan.Value.Preview.WillAssign.Should().Be(2);
    }

    [Fact]
    public async Task Withdrawn_students_are_never_swept_in()
    {
        // "Retrait" carries year 0, so a naive "année ≤ 2" would include people who left.
        await using var db = TestHarness.NewContext("target-retrait");
        SeedTexts(db);
        db.SeedRegistration("Parti", "Retrait", null, TestHarness.CurrentYearId, Level(db, 0));
        db.SeedRegistration("Reste", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(NewText, Rule(2), default);

        plan.Value.Preview.TotalMatched.Should().Be(1);
    }

    [Fact]
    public async Task Another_programme_is_out_of_scope()
    {
        await using var db = TestHarness.NewContext("target-programme");
        SeedTexts(db);
        db.SeedRegistration("Med", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        db.SeedRegistration("Pharm", "Deuxième", null, TestHarness.CurrentYearId,
            Level(db, 2, AcademicProgram.Pharmacie));
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(NewText, Rule(2), default);

        plan.Value.Preview.TotalMatched.Should().Be(1, "the rule names Médecine");
    }

    // ── The disagreement the faculty must settle ─────────────────────────────

    [Fact]
    public async Task A_student_whose_entry_predates_the_text_is_reported_not_assigned()
    {
        await using var db = TestHarness.NewContext("target-contradiction");
        SeedTexts(db);
        Enrol(db, "Redoublant", 2, Year2023, TestHarness.CurrentYearId);
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(NewText, Rule(2), default);

        plan.Value.Preview.WillAssign.Should().Be(0);
        plan.Value.Preview.EntryPredatesText.Should().Be(1);
        plan.Value.Preview.NeedsAttention.Should().ContainSingle()
            .Which.Status.Should().Be(CnpnTargetRowStatus.EntryPredatesText);
    }

    [Fact]
    public async Task The_faculty_can_choose_to_include_them()
    {
        await using var db = TestHarness.NewContext("target-contradiction-included");
        SeedTexts(db);
        Enrol(db, "Redoublant", 2, Year2023, TestHarness.CurrentYearId);
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(
            NewText, Rule(2, includeContradictions: true), default);

        plan.Value.Preview.WillAssign.Should().Be(1,
            "the system reports the disagreement; it does not get to settle it");
        plan.Value.Preview.EntryPredatesText.Should().Be(0);
    }

    // ── Stickiness survives the bulk path ────────────────────────────────────

    [Fact]
    public async Task A_confirmed_stamp_on_another_text_is_never_moved_in_bulk()
    {
        await using var db = TestHarness.NewContext("target-conflict");
        SeedTexts(db);
        var reg = db.SeedRegistration("Confirmé", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        reg.Student.AssignCnpnVersion(OldText, isInferred: false);
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(NewText, Rule(2), default);

        plan.Value.Preview.WillAssign.Should().Be(0);
        plan.Value.Preview.ConfirmedOnAnotherText.Should().Be(1);
        plan.Value.Preview.CanApply.Should().BeFalse();

        var student = await db.Students.SingleAsync();
        student.CnpnVersionId.Should().Be(OldText, "bulk must not defeat the per-student guard");
    }

    [Fact]
    public async Task An_inferred_stamp_is_upgraded_rather_than_blocked()
    {
        // This is how scolarité confirms the ~2,200 assignments the backfill could only deduce.
        await using var db = TestHarness.NewContext("target-upgrade");
        SeedTexts(db);
        var reg = db.SeedRegistration("Déduit", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        reg.Student.AssignCnpnVersion(NewText, isInferred: true);
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(NewText, Rule(2), default);

        plan.Value.Preview.WillAssign.Should().Be(1);
        plan.Value.Preview.AlreadyOnThisText.Should().Be(0);
    }

    [Fact]
    public async Task A_student_already_confirmed_on_this_text_is_left_alone()
    {
        await using var db = TestHarness.NewContext("target-noop");
        SeedTexts(db);
        var reg = db.SeedRegistration("Déjà", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        reg.Student.AssignCnpnVersion(NewText, isInferred: false);
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(NewText, Rule(2), default);

        plan.Value.Preview.AlreadyOnThisText.Should().Be(1);
        plan.Value.Preview.WillAssign.Should().Be(0);
        plan.Value.Preview.CanApply.Should().BeFalse("re-running a settled rule writes nothing");
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Targeting_a_text_that_governs_no_intake_is_refused()
    {
        await using var db = TestHarness.NewContext("target-no-intake");
        SeedTexts(db);
        db.SeedCnpnVersion(93, "2175.22", totalYears: 7, appliesFromAcademicYearId: null);
        db.SeedRegistration("Qui", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(93, Rule(2), default);

        plan.IsFailure.Should().BeTrue();
        plan.Error.Code.Should().Be("Cnpn.TargetTextGovernsNoIntake");
    }

    [Fact]
    public async Task Targeting_a_text_of_another_programme_is_refused()
    {
        await using var db = TestHarness.NewContext("target-text-programme");
        SeedTexts(db);
        db.SeedCnpnVersion(95, "PHARM", totalYears: 6,
            program: AcademicProgram.Pharmacie, appliesFromAcademicYearId: Year2024);
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(95, Rule(2), default);

        plan.IsFailure.Should().BeTrue();
        plan.Error.Code.Should().Be("Cnpn.TargetProgramMismatch");
    }

    [Fact]
    public async Task An_unknown_text_is_refused()
    {
        await using var db = TestHarness.NewContext("target-unknown");
        SeedTexts(db);
        await db.SaveChangesAsync();

        var plan = await Planner(db).PlanAsync(999, Rule(2), default);

        plan.IsFailure.Should().BeTrue();
        plan.Error.Code.Should().Be("CnpnVersions.NotFound");
    }

    // ── Apply writes exactly what the preview promised ───────────────────────

    [Fact]
    public async Task Applying_writes_the_previewed_population_and_nothing_else()
    {
        await using var db = TestHarness.NewContext("target-apply");
        SeedTexts(db);
        db.SeedRegistration("Neuf", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        var confirmed = db.SeedRegistration("Bloqué", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        confirmed.Student.AssignCnpnVersion(OldText, isInferred: false);
        await db.SaveChangesAsync();

        var handler = new ApplyCnpnTargetCommandHandler(db, Planner(db), db.AdminAuthorizer());
        var result = await handler.Handle(new ApplyCnpnTargetCommand(NewText, Rule(2)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.WillAssign.Should().Be(1);
        result.Value.ConfirmedOnAnotherText.Should().Be(1);

        (await db.Students.CountAsync(s => s.CnpnVersionId == NewText)).Should().Be(1);
        (await db.Students.CountAsync(s => s.CnpnVersionId == OldText)).Should()
            .Be(1, "the confirmed student stayed where they were");
    }

    [Fact]
    public async Task An_applied_rule_records_a_decision_not_a_deduction()
    {
        await using var db = TestHarness.NewContext("target-apply-confirms");
        SeedTexts(db);
        var reg = db.SeedRegistration("Déduit", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        reg.Student.AssignCnpnVersion(NewText, isInferred: true);
        await db.SaveChangesAsync();

        await new ApplyCnpnTargetCommandHandler(db, Planner(db), db.AdminAuthorizer())
            .Handle(new ApplyCnpnTargetCommand(NewText, Rule(2)), default);

        var student = await db.Students.SingleAsync();
        student.CnpnAssignmentIsInferred.Should().BeFalse(
            "the faculty looked at the population and said yes");
    }

    [Fact]
    public async Task Applying_a_rule_that_changes_nothing_is_refused_rather_than_silently_empty()
    {
        await using var db = TestHarness.NewContext("target-apply-empty");
        SeedTexts(db);
        var reg = db.SeedRegistration("Déjà", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        reg.Student.AssignCnpnVersion(NewText, isInferred: false);
        await db.SaveChangesAsync();

        var result = await new ApplyCnpnTargetCommandHandler(db, Planner(db), db.AdminAuthorizer())
            .Handle(new ApplyCnpnTargetCommand(NewText, Rule(2)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cnpn.TargetNothingToApply");
    }

    [Fact]
    public async Task Only_administration_may_target_a_promotion()
    {
        await using var db = TestHarness.NewContext("target-authz");
        SeedTexts(db);
        db.SeedRegistration("Qui", "Deuxième", null, TestHarness.CurrentYearId, Level(db, 2));
        await db.SaveChangesAsync();

        var result = await new ApplyCnpnTargetCommandHandler(db, Planner(db), db.StrangerAuthorizer())
            .Handle(new ApplyCnpnTargetCommand(NewText, Rule(2)), default);

        result.IsFailure.Should().BeTrue();
        (await db.Students.SingleAsync()).CnpnVersionId.Should().BeNull();
    }
}
