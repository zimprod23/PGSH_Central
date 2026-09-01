using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The chef worklist is scoped to the current academic year by default — but only because it can say
// what that costs.
//
// Two live incidents came from making year scoping implicit and silent: the worklist was first
// scoped by the registration's academic year, then by the current year's calendar span, and each
// time a mismatch between that bookkeeping and the real rotation dates blanked a chef's entire list.
// The lesson was never "years are the wrong filter" — it was that a filter which can empty a screen
// must announce what it removed. So the rule is now:
//
//   · the year narrows, the STATE bounds — the list can never grow unbounded whatever the year does;
//   · the year is READ off the period's registration, never inferred from its dates;
//   · every hidden row is counted into OutsideYearCount, for the slice being shown;
//   · AllYears is the explicit escape, and one click reaches it;
//   · anything unresolvable (no current year, a dead year id) falls back to spanning every year,
//     never to an empty list.
//
// These tests are the guarantee. If OutsideYearCount ever stops being reported, the 2026-08 incident
// is back — silently.
public class ChefWorklistYearScopeTests
{
    private const int ServiceId      = 1;
    private const int CurrentYearId  = 4;   // flagged IsCurrent: 2025-09 → 2026-08
    private const int PreviousYearId = 3;   // 2024-09 → 2025-08

    private static readonly Guid ChefIdentity = Guid.NewGuid();

    /// <summary>
    /// Reproduces the shape that broke twice in production: the year flagged current covers
    /// 2025-09 → 2026-08, and the rotation runs on dates the caller passes in — possibly outside it.
    /// <paramref name="registrationYearId"/> is what the scoping actually reads, so it is a
    /// parameter rather than a consequence of the dates: the two disagree on 6.7% of the real base
    /// and every test here is about which of them the handler believes.
    /// </summary>
    private static async Task SeedAsync(
        ApplicationDbContext db, DateOnly periodStart, DateOnly periodEnd,
        int registrationYearId = PreviousYearId)
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
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup, registrationYearId);
        var assignment = db.SeedAssignment(registration, cohort);
        db.SeedPeriod(assignment, service, periodStart, periodEnd);

        await db.SaveChangesAsync();
    }

    private static GetMyServicePeriodsQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity)));

    // ─── The default is the current year ──────────────────────────────────────

    [Fact]
    public async Task An_omitted_year_resolves_to_the_one_flagged_current_and_says_so()
    {
        await using var db = TestHarness.NewContext("worklist-default-year");
        await SeedAsync(db, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), CurrentYearId);

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AcademicYearId.Should().Be(
            CurrentYearId,
            "the year is echoed back because the caller did not choose it — a selector left to " +
            "work out which year it is displaying is a second place for that answer to live");
        result.Value.Page.Items.Should().ContainSingle();
        result.Value.OutsideYearCount.Should().Be(0, "nothing was hidden");
    }

    // ⚠ The year is the registration's, and the dates do not get a vote. Measured 2026-08-30, the
    // two disagree on 7 030 of 105 626 periods — 5 043 of them 2019-2020 stages that ran into
    // 2020-2021 because that year was postponed. A date rule cannot tell a year that ran late from
    // the next year's work; the registration says which year the faculty enrolled the student for.
    [Fact]
    public async Task The_year_is_the_registration_s_even_when_the_dates_fall_in_another()
    {
        await using var db = TestHarness.NewContext("worklist-old-registration");
        // Registered 2024-2025, served 03/2026 — the shape of a postponed year.
        await SeedAsync(db, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), PreviousYearId);

        var byRegistration = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: PreviousYearId), default);
        var byDates = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: CurrentYearId), default);

        byRegistration.Value.Page.Items.Should().ContainSingle("that is the year he was enrolled in");
        byRegistration.Value.Page.Items.Single().AcademicGroupLabel.Should().Be("Groupe 10");
        byDates.Value.Page.Items.Should().BeEmpty("where the dates happen to fall decides nothing");
        byDates.Value.OutsideYearCount.Should().Be(1);
    }

    // ─── …and nothing it hides can hide silently ──────────────────────────────

    // ⚠ The test that stands in for both incidents. The rotation is real, live, and in this chef's
    // service; it simply belongs to another year. Under the old implicit scoping his screen went
    // blank with nothing on it to say why. It is legitimate for the default view not to show this
    // row — but never legitimate for the chef to be unable to find out it exists.
    //
    // Note what is no longer possible: a period outside EVERY year. Registration.AcademicYearId is
    // NOT NULL behind a RESTRICT foreign key, so the years partition the periods by construction —
    // there is no row for the date rule's "runs past the end of the calendar" case to strand.
    [Fact]
    public async Task A_rotation_of_another_year_is_reported_not_silently_dropped()
    {
        await using var db = TestHarness.NewContext("worklist-outside-years");
        await SeedAsync(db, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), PreviousYearId);

        var scoped = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);

        scoped.IsSuccess.Should().BeTrue();
        scoped.Value.Page.Items.Should().BeEmpty("it belongs to 2024-2025");
        scoped.Value.OutsideYearCount.Should().Be(
            1,
            "an empty slice that cannot say 'there is one more, elsewhere' is the 2026-08 incident");
    }

    [Fact]
    public async Task A_rotation_from_a_past_year_is_reported_as_outside_the_current_one()
    {
        await using var db = TestHarness.NewContext("worklist-past-scoped");
        await SeedAsync(db, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);

        result.Value.Page.Items.Should().BeEmpty();
        result.Value.OutsideYearCount.Should().Be(1);
    }

    // The count is per SLICE, not per service: it answers "is this list short because of the year?",
    // which is only a question about the list being looked at.
    [Fact]
    public async Task The_outside_count_describes_the_requested_state_only()
    {
        await using var db = TestHarness.NewContext("worklist-outside-per-state");
        await SeedAsync(db, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        var underway = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);
        var planned = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(State: ServicePeriodState.Planned), default);

        underway.Value.OutsideYearCount.Should().Be(1, "the hidden rotation is underway");
        planned.Value.OutsideYearCount.Should().Be(0, "and there is nothing planned anywhere");
    }

    // ─── The escape hatch ─────────────────────────────────────────────────────

    [Fact]
    public async Task AllYears_spans_every_year_and_has_nothing_left_outside()
    {
        await using var db = TestHarness.NewContext("worklist-all-years");
        await SeedAsync(db, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(AllYears: true), default);

        result.Value.Page.Items.Should().ContainSingle(
            "the row the default view reported as outside must be reachable in one click");
        result.Value.AcademicYearId.Should().BeNull("no year bounds this read");
        result.Value.OutsideYearCount.Should().Be(0, "there is no outside when nothing is scoped out");
    }

    // Both together can only come from a caller that has just changed its mind — the widening one is
    // the deliberate act, so it wins.
    [Fact]
    public async Task AllYears_wins_over_an_explicit_year()
    {
        await using var db = TestHarness.NewContext("worklist-all-years-wins");
        await SeedAsync(db, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        var result = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: CurrentYearId, AllYears: true), default);

        result.Value.Page.Items.Should().ContainSingle();
        result.Value.AcademicYearId.Should().BeNull();
    }

    // ─── Explicit years scope on dates ────────────────────────────────────────

    [Fact]
    public async Task An_explicit_year_scopes_to_the_rotations_running_inside_its_span()
    {
        await using var db = TestHarness.NewContext("worklist-explicit-year");
        await SeedAsync(db, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        var inThatYear = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: PreviousYearId), default);
        var inTheOther = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: CurrentYearId), default);

        inThatYear.Value.Page.Items.Should().ContainSingle("the rotation ran inside 2024-2025");
        inThatYear.Value.AcademicYearId.Should().Be(PreviousYearId);
        inTheOther.Value.Page.Items.Should().BeEmpty("it did not run inside 2025-2026");
        inTheOther.Value.OutsideYearCount.Should().Be(1, "and the other year is where it is");
    }

    // ⚠ The defect as reported, 2026-08-30, and the reason the date rule had to go. A chef looking
    // at 2026-2027 was shown 41 rotations of 6ᵉ année Pédiatrie under « à évaluer » — a promotion
    // with no 2026-2027 partitioning, no planning and nothing published. They were 2025-2026
    // rotations that ran 08 jul → 08 sep 2026, and a date predicate filed them under the new year
    // because they finished eight days into it. Their registration always said 2025-2026; nothing
    // had to be inferred, and reading it is what makes the case impossible rather than merely fixed.
    [Fact]
    public async Task A_rotation_of_a_past_year_running_into_this_one_is_not_this_year_s_work()
    {
        await using var db = TestHarness.NewContext("worklist-tail-of-previous-year");
        // Registered 2024-2025, ran 08 jul → 08 sep 2025 — the shape measured on the base.
        await SeedAsync(db, new DateOnly(2025, 7, 8), new DateOnly(2025, 9, 8), PreviousYearId);

        var newYear = await Handler(db)
            .Handle(new GetMyServicePeriodsQuery(AcademicYearId: CurrentYearId), default);

        newYear.Value.Page.Items.Should().BeEmpty(
            "a promotion with no planning for this year must not appear to have rotations in it");
        newYear.Value.OutsideYearCount.Should().Be(
            1, "it is the previous year's work, and the chef is told where it is");
    }

    // ─── Unresolvable years widen, never empty ────────────────────────────────

    [Fact]
    public async Task An_unknown_academic_year_leaves_the_worklist_unscoped_rather_than_empty()
    {
        await using var db = TestHarness.NewContext("worklist-unknown-year");
        await SeedAsync(db, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(AcademicYearId: 999), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Items.Should().ContainSingle(
            "a bad year id must not be indistinguishable from 'this chef has no work'");
        result.Value.AcademicYearId.Should().BeNull();
    }

    // ⚠ IX_AcademicYear_IsCurrent is unique but filtered, so "no row flagged current" is a state the
    // table can be in — between years, or after a bad delete. Every writing handler is right to
    // refuse there. This read must not: it is the screen that shows people what they are doing today,
    // and a missing bookkeeping flag is the worst possible reason to blank it.
    [Fact]
    public async Task With_no_year_flagged_current_the_worklist_spans_every_year()
    {
        await using var db = TestHarness.NewContext("worklist-no-current-year");
        await SeedAsync(db, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

        // Relinquished after the fact rather than seeded unflagged: SeedCatalog writes the current
        // year itself, so a flag passed into the seed would be quietly overwritten and this test
        // would pass for the wrong reason.
        foreach (var year in db.AcademicYears.Local.ToList())
            year.Relinquish();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetMyServicePeriodsQuery(), default);

        result.IsSuccess.Should().BeTrue("a read must not fail on a flag it does not write");
        result.Value.Page.Items.Should().ContainSingle();
        result.Value.AcademicYearId.Should().BeNull();
    }
}
