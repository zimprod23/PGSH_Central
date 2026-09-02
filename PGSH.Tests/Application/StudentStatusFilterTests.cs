using FluentAssertions;
using PGSH.Application.Students.GetMany;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Filtering the roll by the verdict recorded on the year — how the 1 217 diplômés of a réinscription
/// become findable from a screen rather than only from a downloaded file.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Every case here is the same trap in a different costume: a predicate satisfied by two
/// <em>different</em> registrations.</b> 2 635 students in this base have repeated, and the final year
/// is re-registered every September until the thesis is defended — so a student holding a
/// « Diplômé » on one year and an « Active » on another is the ordinary case, not an edge one. Asked
/// as two independent <c>Any</c>s, « diplômé » ∧ « 2026-2027 » returns him, and the answer is
/// plausible enough to be believed.</para>
///
/// <para>Measured on the live base for the level/year pair the same rule already governs:
/// « 5ᵉ année Médecine, 2026-2027 » is 833 students as one <c>Any</c> and 2 127 as two.</para>
/// </remarks>
public class StudentStatusFilterTests
{
    private static readonly DateTime Recorded = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    private const int ThisYear = TestHarness.CurrentYearId;
    private const int LastYear = TestHarness.PreviousYearId;

    private static async Task<List<StudentSummaryResponse>> QueryAsync(
        ApplicationDbContext db, RegistrationStatus? status, int? yearId = null, int? levelId = null)
    {
        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(null, null, null, null,
                LevelId: levelId, AcademicYearId: yearId, Status: status),
            default);

        result.IsSuccess.Should().BeTrue();
        return result.Value.Items.ToList();
    }

    /// <summary>
    /// The student who graduated last year and is registered again this year. Both facts are true of
    /// him and neither is true of the <em>same</em> registration.
    /// </summary>
    [Fact]
    public async Task A_verdict_and_a_year_must_hold_on_the_same_registration()
    {
        await using var db = TestHarness.NewContext(nameof(A_verdict_and_a_year_must_hold_on_the_same_registration));

        db.SeedCatalog();
        db.SeedAcademicYear(LastYear, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        // ⚠ One student, two registrations. Two *students* would satisfy the assertion below whatever
        // the handler does — SeedRegistration mints a new student on every call, which is precisely
        // how a test for this trap passes for the wrong reason.
        var returning = db.SeedRegistration("Amine", "Returning", academicYearId: LastYear);
        returning.RecordYearOutcome(RegistrationStatus.Graduated, RegistrationOutcomeSource.Declared, null, Recorded);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = ThisYear, LevelId = TestHarness.LevelId,
            StudentId = returning.StudentId,
        });

        var graduate = db.SeedRegistration("Nadia", "Graduate", academicYearId: ThisYear);
        graduate.RecordYearOutcome(RegistrationStatus.Graduated, RegistrationOutcomeSource.Declared, null, Recorded);

        await db.SaveChangesAsync();

        var found = await QueryAsync(db, RegistrationStatus.Graduated, yearId: ThisYear);

        found.Should().ContainSingle("only one registration of this year carries the verdict");
        found[0].LastName.Should().Be("Graduate");
    }

    /// <summary>Without a year the question widens to « a-t-il jamais été diplômé », which is a real question.</summary>
    [Fact]
    public async Task A_verdict_without_a_year_spans_every_registration()
    {
        await using var db = TestHarness.NewContext(nameof(A_verdict_without_a_year_spans_every_registration));

        db.SeedCatalog();
        db.SeedAcademicYear(LastYear, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        var past = db.SeedRegistration("Amine", "Past", academicYearId: LastYear);
        past.RecordYearOutcome(RegistrationStatus.Graduated, RegistrationOutcomeSource.Declared, null, Recorded);
        db.SeedRegistration("Sara", "Active", academicYearId: ThisYear);

        await db.SaveChangesAsync();

        (await QueryAsync(db, RegistrationStatus.Graduated))
            .Should().ContainSingle().Which.LastName.Should().Be("Past");
    }

    /// <summary>
    /// ⚠ <c>Excluded</c> is not <c>Failed</c> and <c>Graduated</c> is not <c>Validated</c>: one ends
    /// the cursus, the other repeats or advances. A filter that collapsed either pair would answer a
    /// different question from the one the label promises.
    /// </summary>
    [Fact]
    public async Task The_five_verdicts_are_not_collapsed_into_each_other()
    {
        await using var db = TestHarness.NewContext(nameof(The_five_verdicts_are_not_collapsed_into_each_other));

        db.SeedCatalog();

        foreach (var (name, outcome) in new[]
                 {
                     ("Graduated", RegistrationStatus.Graduated),
                     ("Validated", RegistrationStatus.Validated),
                     ("Failed",    RegistrationStatus.Failed),
                     ("Excluded",  RegistrationStatus.Excluded),
                     ("Withdrawn", RegistrationStatus.Withdrawn),
                 })
        {
            var registration = db.SeedRegistration("X", name, academicYearId: ThisYear);
            registration.RecordYearOutcome(outcome, RegistrationOutcomeSource.Declared, null, Recorded);
        }

        await db.SaveChangesAsync();

        foreach (var outcome in new[]
                 {
                     RegistrationStatus.Graduated, RegistrationStatus.Validated,
                     RegistrationStatus.Failed, RegistrationStatus.Excluded,
                     RegistrationStatus.Withdrawn,
                 })
        {
            (await QueryAsync(db, outcome, yearId: ThisYear))
                .Should().ContainSingle().Which.LastName.Should().Be(outcome.ToString());
        }
    }

    /// <summary>
    /// The verdict narrows on top of the promotion, and all three conditions still have to meet on
    /// one row: the 4ᵉ année student who once failed the 3ᵉ is not a « redoublant de 3ᵉ année » this
    /// year.
    /// </summary>
    [Fact]
    public async Task A_promotion_a_year_and_a_verdict_all_meet_on_one_registration()
    {
        await using var db = TestHarness.NewContext(nameof(A_promotion_a_year_and_a_verdict_all_meet_on_one_registration));

        db.SeedCatalog();
        db.SeedLevel(4, "4ème année", 4);
        db.SeedAcademicYear(LastYear, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

        // Failed the 3ᵉ année last year, sitting in the 4ᵉ this year — matches each condition on a
        // different row, and must match none of them together.
        var repeated = db.SeedRegistration("Omar", "MovedOn", academicYearId: LastYear, levelId: TestHarness.LevelId);
        repeated.RecordYearOutcome(RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, Recorded);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = ThisYear, LevelId = 4,
            StudentId = repeated.StudentId,
        });

        var repeating = db.SeedRegistration("Hind", "Repeating", academicYearId: ThisYear, levelId: TestHarness.LevelId);
        repeating.RecordYearOutcome(RegistrationStatus.Failed, RegistrationOutcomeSource.Declared, null, Recorded);

        await db.SaveChangesAsync();

        var found = await QueryAsync(db, RegistrationStatus.Failed, yearId: ThisYear, levelId: TestHarness.LevelId);

        found.Should().ContainSingle();
        found[0].LastName.Should().Be("Repeating");
    }

    /// <summary>
    /// The control. A filter that returned nothing whatever it was asked would satisfy every
    /// assertion above and prove none of them.
    /// </summary>
    [Fact]
    public async Task No_verdict_asked_returns_the_whole_roll()
    {
        await using var db = TestHarness.NewContext(nameof(No_verdict_asked_returns_the_whole_roll));

        db.SeedCatalog();
        var graduate = db.SeedRegistration("Nadia", "Graduate", academicYearId: ThisYear);
        graduate.RecordYearOutcome(RegistrationStatus.Graduated, RegistrationOutcomeSource.Declared, null, Recorded);
        db.SeedRegistration("Sara", "Active", academicYearId: ThisYear);
        await db.SaveChangesAsync();

        (await QueryAsync(db, status: null, yearId: ThisYear)).Should().HaveCount(2);
    }
}
