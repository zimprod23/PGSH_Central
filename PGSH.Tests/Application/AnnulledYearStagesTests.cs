using FluentAssertions;
using PGSH.Application.Stages.Progression;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A failed year is annulled, and so is everything served inside it.
///
/// <para><b>The faculty's rule, settled 2026-09-01.</b> A redoublant repeats the year from scratch —
/// including the stages he passed. So an attempt made in a year the déliberation failed establishes
/// nothing: not an acquisition, and not a debt either. A stage failed in a year the student
/// <i>cleared</i> is the opposite case and stays owed, to be settled by revalidation.</para>
///
/// <para>⚠ <b>The case that forced it.</b> Passing a stage, failing the year, repeating it and
/// failing that same stage the second time. Read without this rule the student has « une tentative
/// validée », the stage is cleared for good, and <c>FinalYearGuard</c> lets him into his last year
/// on the strength of a year the faculty had struck out — while the last thing he actually did was
/// fail it.</para>
///
/// <para>The marks drive the real lifecycle (<c>Start</c> → <c>CompletePeriod</c> →
/// <c>SubmitEvaluation</c>) through <c>SeedGradedAssignment</c>, because
/// <c>InternshipAssignment.Result</c> is written by the domain and not assignable from a test.</para>
/// </summary>
public class AnnulledYearStagesTests
{
    private const int Year2025 = TestHarness.CurrentYearId;
    private const int Year2026 = 40;
    private const decimal Pass = 14m;
    private const decimal Fail = 7m;

    private static ApplicationDbContext Seed(string name)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog();
        db.SeedAcademicYear(Year2026, "2026-2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));
        db.SeedService(1, "Service de Cardiologie");

        // One cohorte per year, both on the shared catalogue stage: the student sits the same stage
        // twice, which is the whole point.
        db.SeedCohort(stage, groupId: 901, groupLabel: "G901");
        db.SeedCohortFor(stage, db.SeedGroup(902, groupNumber: 2, academicYearId: Year2026), cohortId: 902);

        return db;
    }

    /// <summary>The student's first year, with a verdict and a mark on the shared stage.</summary>
    private static Registration FirstYear(
        ApplicationDbContext db, decimal mark, RegistrationStatus outcome)
    {
        var registration = db.SeedRegistration("Karim", "Bennani", academicYearId: Year2025);
        registration.Status = outcome;

        db.SeedGradedAssignment(
            registration, db.Cohorts.Local.First(c => c.Id == 901),
            db.Services.Local.First(), mark);

        return registration;
    }

    /// <summary>The year he redoes, on the same student — a second registration, as a repeat is.</summary>
    private static void RepeatYear(
        ApplicationDbContext db, Student student, decimal mark, RegistrationStatus outcome)
    {
        var registration = new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = Year2026, LevelId = TestHarness.LevelId,
            StudentId = student.Id, Student = student, Status = outcome,
        };
        db.Registrations.Add(registration);

        db.SeedGradedAssignment(
            registration, db.Cohorts.Local.First(c => c.Id == 902),
            db.Services.Local.First(), mark, from: new DateOnly(2026, 10, 1));
    }

    private static Task<IReadOnlyList<OutstandingStageFinder.Debt>> OwedBy(
        ApplicationDbContext db, Guid studentId) =>
        new OutstandingStageFinder(db).ForStudentAsync(studentId, default);

    // -- The rule itself -------------------------------------------------------

    [Fact]
    public void Only_a_failed_year_annuls_what_was_served_in_it()
    {
        RegistrationStatus.Failed.AnnulsItsStages().Should().BeTrue();

        RegistrationStatus.Validated.AnnulsItsStages().Should().BeFalse();
        RegistrationStatus.Graduated.AnnulsItsStages().Should().BeFalse();

        RegistrationStatus.Active.AnnulsItsStages().Should().BeFalse(
            "the legacy import wrote Active on every historical registration — reading silence as a "
            + "failure would make the whole imported cursus outstanding");
        RegistrationStatus.Pending.AnnulsItsStages().Should().BeFalse();

        // These end the cursus rather than repeat the year: nobody has ruled that what was served
        // before an abandon never happened.
        RegistrationStatus.Withdrawn.AnnulsItsStages().Should().BeFalse();
        RegistrationStatus.Excluded.AnnulsItsStages().Should().BeFalse();
    }

    // -- What a student owes ---------------------------------------------------

    /// <summary>The case the rule exists for: passed it in the year he failed, failed it in the redo.</summary>
    [Fact]
    public async Task A_pass_inside_a_failed_year_does_not_clear_the_stage()
    {
        await using var db = Seed(nameof(A_pass_inside_a_failed_year_does_not_clear_the_stage));

        var first = FirstYear(db, Pass, RegistrationStatus.Failed);
        RepeatYear(db, first.Student, Fail, RegistrationStatus.Active);
        await db.SaveChangesAsync();

        var owed = await OwedBy(db, first.StudentId);

        owed.Should().ContainSingle("the annulled pass drops out and the surviving attempt failed")
            .Which.StageId.Should().Be(TestHarness.StageId);
    }

    /// <summary>The same two attempts with the first year <i>cleared</i>: the pass counts, as always.</summary>
    [Fact]
    public async Task A_pass_inside_a_year_that_was_cleared_still_clears_the_stage()
    {
        await using var db = Seed(nameof(A_pass_inside_a_year_that_was_cleared_still_clears_the_stage));

        var first = FirstYear(db, Pass, RegistrationStatus.Validated);
        RepeatYear(db, first.Student, Fail, RegistrationStatus.Active);
        await db.SaveChangesAsync();

        var owed = await OwedBy(db, first.StudentId);

        owed.Should().BeEmpty("a stage once acquired is never repeated, whichever year earned it");
    }

    /// <summary>
    /// ⚠ The filter drops attempts, it never turns one into a debt. A stage failed only inside an
    /// annulled year is not owed: the student is repeating that year and will serve it again.
    /// </summary>
    [Fact]
    public async Task A_failure_inside_a_failed_year_is_not_carried_forward_as_a_debt()
    {
        await using var db = Seed(nameof(A_failure_inside_a_failed_year_is_not_carried_forward_as_a_debt));

        var only = FirstYear(db, Fail, RegistrationStatus.Failed);
        await db.SaveChangesAsync();

        var owed = await OwedBy(db, only.StudentId);

        owed.Should().BeEmpty(
            "the year is annulled in both directions — he owes nothing from it, he simply redoes it");
    }

    /// <summary>The ordinary carried credit, unchanged: failed a stage in a year he cleared.</summary>
    [Fact]
    public async Task A_failure_inside_a_year_that_was_cleared_is_still_owed()
    {
        await using var db = Seed(nameof(A_failure_inside_a_year_that_was_cleared_is_still_owed));

        var passed = FirstYear(db, Fail, RegistrationStatus.Validated);
        await db.SaveChangesAsync();

        var owed = await OwedBy(db, passed.StudentId);

        owed.Should().ContainSingle("he moved up carrying the stage — that is what revalidation settles");
    }

    /// <summary>
    /// The legacy shape: every imported registration reads <c>Active</c> because no verdict was ever
    /// recorded for it. Nothing about those students may change.
    /// </summary>
    [Fact]
    public async Task An_unpronounced_year_behaves_exactly_as_before()
    {
        await using var db = Seed(nameof(An_unpronounced_year_behaves_exactly_as_before));

        var legacy = FirstYear(db, Pass, RegistrationStatus.Active);
        await db.SaveChangesAsync();

        var owed = await OwedBy(db, legacy.StudentId);

        owed.Should().BeEmpty();
    }
}
