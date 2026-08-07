using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Year scoping on the chef worklist is OPT-IN. Two live incidents came from making it implicit: the
// worklist was first scoped by the registration's academic year, then by the current year's calendar
// span, and each time a mismatch between that bookkeeping and the real rotation dates blanked a
// chef's entire list. The rule now is: no year filter unless the caller asks for one, so live work
// can never be hidden by a stale AcademicYear record.
public class ChefWorklistYearScopeTests
{
    private const int ServiceId      = 1;
    private const int CurrentYearId  = 4;   // flagged IsCurrent, but its span matches no rotation
    private const int PreviousYearId = 3;

    private static readonly Guid ChefIdentity = Guid.NewGuid();

    /// <summary>
    /// Reproduces the shape that broke twice in production: the year flagged current covers
    /// 2025-09 → 2026-08, the registration is tagged an older year, and the rotation actually runs
    /// on dates the caller passes in — possibly outside both.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db, DateOnly periodStart, DateOnly periodEnd)
    {
        db.AcademicYears.AddRange(
            new AcademicYear
            {
                Id = PreviousYearId, Label = "2024-2025", IsCurrent = false,
                StartDate = new DateOnly(2024, 9, 1), EndDate = new DateOnly(2025, 8, 31),
            },
            new AcademicYear
            {
                Id = CurrentYearId, Label = "2025-2026", IsCurrent = true,
                StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
            });

        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(ServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup, PreviousYearId);
        var assignment = db.SeedAssignment(registration, cohort);
        db.SeedPeriod(assignment, service, periodStart, periodEnd);

        await db.SaveChangesAsync();
    }

    private static GetMyServicePeriodsQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity)));

    [Fact]
    public async Task A_rotation_far_outside_every_academic_year_still_reaches_its_chef()
    {
        await using var db = TestHarness.NewContext("worklist-outside-years");
        // Exactly the live incident: seeded years stop at 2026-08 while the rotations run Jun-Sep 2026
        // against a registration tagged an older year.
        await SeedAsync(db, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(
            "a chef must never lose his worklist because the academic-year records are stale");
    }

    [Fact]
    public async Task A_rotation_whose_registration_carries_an_older_year_still_reaches_its_chef()
    {
        await using var db = TestHarness.NewContext("worklist-old-registration");
        await SeedAsync(db, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);

        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().AcademicGroupLabel.Should().Be("Groupe 10");
    }

    [Fact]
    public async Task A_rotation_from_a_past_year_is_still_listed_when_no_year_is_requested()
    {
        await using var db = TestHarness.NewContext("worklist-past-unscoped");
        await SeedAsync(db, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);

        result.Value.Items.Should().ContainSingle(
            "showing an extra past window is strictly better than silently hiding live work");
    }

    [Fact]
    public async Task An_explicit_year_scopes_to_the_rotations_running_inside_its_span()
    {
        await using var db = TestHarness.NewContext("worklist-explicit-year");
        await SeedAsync(db, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        var inThatYear = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: PreviousYearId), default);
        var inTheOther = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: CurrentYearId), default);

        inThatYear.Value.Items.Should().ContainSingle("the rotation ran inside 2024-2025");
        inTheOther.Value.Items.Should().BeEmpty("it did not run inside 2025-2026");
    }

    [Fact]
    public async Task An_explicit_year_scopes_on_the_dates_the_rotation_runs_not_the_registration_tag()
    {
        await using var db = TestHarness.NewContext("worklist-explicit-dates");
        // Registration is tagged 2024-2025 but the rotation runs inside 2025-2026.
        await SeedAsync(db, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var result = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: CurrentYearId), default);

        result.Value.Items.Should().ContainSingle(
            "a retake or late registration still rotates under the chef standing in the service");
    }

    [Fact]
    public async Task A_rotation_straddling_the_year_boundary_counts_as_inside_it()
    {
        await using var db = TestHarness.NewContext("worklist-straddle");
        await SeedAsync(db, new DateOnly(2025, 8, 15), new DateOnly(2025, 9, 15));

        var result = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: CurrentYearId), default);

        result.Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_academic_year_leaves_the_worklist_unscoped_rather_than_empty()
    {
        await using var db = TestHarness.NewContext("worklist-unknown-year");
        await SeedAsync(db, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(AcademicYearId: 999), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(
            "a bad year id must not be indistinguishable from 'this chef has no work'");
    }
}
