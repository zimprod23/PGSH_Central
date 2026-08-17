using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A stage that occupies several périodes can spend them in several services or in one.
///
/// <para>The axis is identical under both modes — the crossover needs <c>kₛ</c> columns whatever
/// happens inside them, and the group really is present for all of them. What differs is the service
/// each column gets and how many evaluations come out the other end. Under
/// <see cref="StageRotationMode.SingleService"/> the run's cells collapse into one continuous
/// <c>ServicePeriod</c>, so a chef enters one mark instead of <c>kₛ</c> identical ones and the roll-up
/// needs no special case: the mean of one mark is that mark.</para>
///
/// <para>⚠ The regression this file exists to prevent is the one the coverage table was introduced
/// for: a period covers <c>kₛ</c> cells but <c>ServicePeriod.CohortSlotAssignmentId</c> names only the
/// first, so any guard reading that FK finds the trailing cells of a published run apparently free.
/// </para>
/// </summary>
public class SingleServiceRotationTests
{
    private const int ServiceA = 1;
    private const int ServiceB = 2;
    private const int CohortId = 10;

    private static readonly DateOnly P1Start = new(2025, 11, 3);
    private static readonly DateOnly P1End   = new(2025, 11, 28);
    private static readonly DateOnly P2Start = new(2025, 12, 1);
    private static readonly DateOnly P2End   = new(2025, 12, 26);
    private static readonly DateOnly P3Start = new(2026, 1, 5);
    private static readonly DateOnly P3End   = new(2026, 1, 30);

    private static SchedulePublisher Publisher(ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db));

    /// <summary>A three-column stage, one cohort, one student — the 5ᵉ-année Gynécologie shape.</summary>
    private static async Task<(Stage Stage, Cohort Cohort, Service A, Service B)> SeedRunAsync(
        ApplicationDbContext db, StageRotationMode mode, int students = 1)
    {
        var stage = db.SeedCatalog();
        stage.RotationMode = mode;

        var a = db.SeedService(ServiceA, "Maternité Souissi");
        var b = db.SeedService(ServiceB, "Maternité Les Orangers");
        a.Capacity = 200;
        b.Capacity = 200;

        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlot(stage, 100, 1, P1Start, P1End);
        db.SeedSlot(stage, 200, 2, P2Start, P2End);
        db.SeedSlot(stage, 300, 3, P3Start, P3End);

        for (int i = 0; i < students; i++)
            db.SeedAssignment(db.SeedRegistration($"E{i}", "Test", cohort.AcademicGroup), cohort);

        await db.SaveChangesAsync();
        return (stage, cohort, a, b);
    }

    private static void PlaceRunIn(ApplicationDbContext db, Cohort cohort, Service service)
    {
        var slots = db.StageSlots.OrderBy(s => s.PeriodNumber).ToList();
        for (int i = 0; i < slots.Count; i++)
            db.SeedSlotAssignment(i + 1, cohort, slots[i], service);
    }

    // ─── Publishing: the collapse ────────────────────────────────────────────

    [Fact]
    public async Task A_single_service_run_publishes_as_one_period_spanning_the_whole_run()
    {
        await using var db = TestHarness.NewContext(nameof(A_single_service_run_publishes_as_one_period_spanning_the_whole_run));
        var (_, cohort, a, _) = await SeedRunAsync(db, StageRotationMode.SingleService);
        PlaceRunIn(db, cohort, a);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsSuccess.Should().BeTrue();

        var periods = await db.ServicePeriods.ToListAsync();
        periods.Should().ContainSingle("three columns in one service are one continuous stay");
        periods[0].StartDate.Should().Be(P1Start);
        periods[0].EndDate.Should().Be(P3End, "the stay runs to the end of the last column of the run");
        periods[0].ServiceId.Should().Be(ServiceA);
    }

    [Fact]
    public async Task The_collapsed_period_records_every_cell_it_covers()
    {
        await using var db = TestHarness.NewContext(nameof(The_collapsed_period_records_every_cell_it_covers));
        var (_, cohort, a, _) = await SeedRunAsync(db, StageRotationMode.SingleService);
        PlaceRunIn(db, cohort, a);
        await db.SaveChangesAsync();

        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        var coverage = await db.ServicePeriodSlotCoverage.ToListAsync();
        coverage.Should().HaveCount(3, "one row per cell of the run, not one per period");
        coverage.Select(c => c.CohortSlotAssignmentId).Should().BeEquivalentTo([1, 2, 3]);

        (await db.ServicePeriods.SingleAsync()).CohortSlotAssignmentId
            .Should().Be(1, "the FK names the lead cell; coverage names all of them");
    }

    [Fact]
    public async Task Per_period_mode_still_publishes_one_period_per_cell_and_covers_each()
    {
        await using var db = TestHarness.NewContext(nameof(Per_period_mode_still_publishes_one_period_per_cell_and_covers_each));
        var (_, cohort, a, _) = await SeedRunAsync(db, StageRotationMode.PerPeriod);
        PlaceRunIn(db, cohort, a);
        await db.SaveChangesAsync();

        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        (await db.ServicePeriods.CountAsync()).Should().Be(3);
        (await db.ServicePeriodSlotCoverage.CountAsync()).Should().Be(3,
            "coverage is written under both modes, so the guards read one table not two");
    }

    [Fact]
    public async Task A_service_change_inside_the_window_breaks_the_run_in_two()
    {
        // A cell edited by hand to another service is two stays. Merging across it would produce one
        // period whose service is a lie for part of its span.
        await using var db = TestHarness.NewContext(nameof(A_service_change_inside_the_window_breaks_the_run_in_two));
        var (_, cohort, a, b) = await SeedRunAsync(db, StageRotationMode.SingleService);
        var slots = db.StageSlots.OrderBy(s => s.PeriodNumber).ToList();
        db.SeedSlotAssignment(1, cohort, slots[0], a);
        db.SeedSlotAssignment(2, cohort, slots[1], a);
        db.SeedSlotAssignment(3, cohort, slots[2], b);
        await db.SaveChangesAsync();

        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        var periods = await db.ServicePeriods.OrderBy(p => p.StartDate).ToListAsync();
        periods.Should().HaveCount(2);
        periods[0].ServiceId.Should().Be(ServiceA);
        periods[0].EndDate.Should().Be(P2End);
        periods[1].ServiceId.Should().Be(ServiceB);
        periods[1].StartDate.Should().Be(P3Start);
    }

    [Fact]
    public async Task A_gap_in_the_period_numbers_breaks_the_run_in_two()
    {
        await using var db = TestHarness.NewContext(nameof(A_gap_in_the_period_numbers_breaks_the_run_in_two));
        var (_, cohort, a, _) = await SeedRunAsync(db, StageRotationMode.SingleService);
        var slots = db.StageSlots.OrderBy(s => s.PeriodNumber).ToList();
        db.SeedSlotAssignment(1, cohort, slots[0], a);   // P1
        db.SeedSlotAssignment(3, cohort, slots[2], a);   // P3 — P2 belongs to another partition
        await db.SaveChangesAsync();

        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        var periods = await db.ServicePeriods.OrderBy(p => p.StartDate).ToListAsync();
        periods.Should().HaveCount(2, "a single stay cannot have a hole in it");
        periods[0].EndDate.Should().Be(P1End);
        periods[1].StartDate.Should().Be(P3Start);
    }

    // ─── Arranging: the run keeps its service ────────────────────────────────

    /// <summary>
    /// Three rosters over three columns and three services.
    ///
    /// <para>⚠ The cohort count has to be at least the column count for either mode to be observable:
    /// <c>shiftPerSlot = n / cycleLength</c> is integer division, so a single cohort over three
    /// columns rotates by 0 and sits in one service under <em>both</em> modes. A comparison seeded
    /// that way passes whatever the arranger does.</para>
    /// </summary>
    private static async Task SeedThreeRostersAsync(ApplicationDbContext db, StageRotationMode mode)
    {
        var stage = db.SeedCatalog();
        stage.RotationMode = mode;

        foreach (var (id, name) in new[] { (1, "Souissi"), (2, "Orangers"), (3, "Ibn Sina") })
        {
            var service = db.SeedService(id, name);
            service.Capacity = 200;
            stage.AllowedServices.Add(service);
        }

        db.SeedSlot(stage, 100, 1, P1Start, P1End);
        db.SeedSlot(stage, 200, 2, P2Start, P2End);
        db.SeedSlot(stage, 300, 3, P3Start, P3End);

        for (int n = 1; n <= 3; n++)
        {
            var group = db.SeedGroup(n, n);
            var cohort = db.SeedCohortFor(stage, group, 100 + n);
            for (int i = 0; i < 4; i++)
                db.SeedAssignment(db.SeedRegistration($"E{n}{i}", "Test", group), cohort);
        }

        await db.SaveChangesAsync();
    }

    private static Task<List<IGrouping<int, CohortSlotAssignment>>> CellsByCohortAsync(ApplicationDbContext db) =>
        db.CohortSlotAssignments.ToListAsync()
            .ContinueWith(t => t.Result.GroupBy(c => c.CohortId).ToList());

    [Fact]
    public async Task Arranging_a_run_gives_every_column_of_it_the_same_service()
    {
        await using var db = TestHarness.NewContext(nameof(Arranging_a_run_gives_every_column_of_it_the_same_service));
        await SeedThreeRostersAsync(db, StageRotationMode.SingleService);

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, [1, 2, 3], null, default);

        result.IsSuccess.Should().BeTrue();

        var byCohort = await CellsByCohortAsync(db);
        byCohort.Should().HaveCount(3);
        foreach (var cohort in byCohort)
        {
            cohort.Should().HaveCount(3, "the group occupies all three columns of the run");
            cohort.Select(c => c.ServiceId).Distinct().Should().ContainSingle(
                "the group stays put for the whole run — that is what the mode means");
        }

        // …and the rosters are still spread across the services rather than piled into one.
        byCohort.Select(g => g.First().ServiceId).Distinct()
            .Should().HaveCountGreaterThan(1, "staying put is per group, not per promotion");
    }

    [Fact]
    public async Task Per_period_mode_still_moves_the_group_between_services()
    {
        await using var db = TestHarness.NewContext(nameof(Per_period_mode_still_moves_the_group_between_services));
        await SeedThreeRostersAsync(db, StageRotationMode.PerPeriod);

        await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, [1, 2, 3], null, default);

        var byCohort = await CellsByCohortAsync(db);
        byCohort.Should().HaveCount(3);
        foreach (var cohort in byCohort)
            cohort.Select(c => c.ServiceId).Distinct().Should().HaveCount(3,
                "rotating S1 → S2 → S3 is the whole point of the default mode");
    }

    [Fact]
    public async Task Arranging_a_single_service_stage_without_a_window_is_refused()
    {
        // The realistic mistake: "auto-arrange this stage" from the stage page. Unscoped, every column
        // the stage owns becomes one run, and a cohort gets a single service for the entire year.
        await using var db = TestHarness.NewContext(nameof(Arranging_a_single_service_stage_without_a_window_is_refused));
        var (stage, _, a, b) = await SeedRunAsync(db, StageRotationMode.SingleService, students: 4);
        stage.AllowedServices.Add(a);
        stage.AllowedServices.Add(b);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SingleServiceRunNotScoped");
        (await db.CohortSlotAssignments.CountAsync()).Should().Be(0, "the refusal writes nothing");
    }

    [Fact]
    public async Task Arranging_a_non_contiguous_window_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Arranging_a_non_contiguous_window_is_refused));
        var (stage, _, a, b) = await SeedRunAsync(db, StageRotationMode.SingleService, students: 4);
        stage.AllowedServices.Add(a);
        stage.AllowedServices.Add(b);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, [1, 3], null, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SingleServiceRunNotContiguous");
    }

    [Fact]
    public async Task A_stage_with_one_column_needs_no_window()
    {
        // k=1 makes the mode a no-op, and demanding a window there would be noise.
        await using var db = TestHarness.NewContext(nameof(A_stage_with_one_column_needs_no_window));
        var stage = db.SeedCatalog();
        stage.RotationMode = StageRotationMode.SingleService;
        var a = db.SeedService(ServiceA, "Maternité Souissi");
        stage.AllowedServices.Add(a);
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlot(stage, 100, 1, P1Start, P1End);
        db.SeedAssignment(db.SeedRegistration("E0", "Test", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Assigned.Should().Be(1);
    }

    // ─── The regression the coverage table exists for ────────────────────────

    [Fact]
    public async Task Re_arranging_never_rewrites_the_trailing_cells_of_a_published_run()
    {
        // Reading ServicePeriod.CohortSlotAssignmentId, the arranger would find cell 1 locked and
        // cells 2 and 3 free, and would move a group that is already standing in the service.
        await using var db = TestHarness.NewContext(nameof(Re_arranging_never_rewrites_the_trailing_cells_of_a_published_run));
        var (stage, cohort, a, b) = await SeedRunAsync(db, StageRotationMode.SingleService, students: 4);
        stage.AllowedServices.Add(a);
        stage.AllowedServices.Add(b);
        PlaceRunIn(db, cohort, a);
        await db.SaveChangesAsync();

        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        var before = await db.CohortSlotAssignments
            .Where(c => c.CohortId == CohortId)
            .Select(c => new { c.Id, c.ServiceId, c.StageSlotId })
            .OrderBy(c => c.Id)
            .ToListAsync();

        await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, [1, 2, 3], null, default);

        var after = await db.CohortSlotAssignments
            .Where(c => c.CohortId == CohortId)
            .Select(c => new { c.Id, c.ServiceId, c.StageSlotId })
            .OrderBy(c => c.Id)
            .ToListAsync();

        after.Should().BeEquivalentTo(before,
            "every cell of a published run is a locked execution record, not just the first");
    }

    [Fact]
    public async Task A_slot_carrying_a_trailing_cell_of_a_published_run_cannot_be_deleted()
    {
        await using var db = TestHarness.NewContext(nameof(A_slot_carrying_a_trailing_cell_of_a_published_run_cannot_be_deleted));
        var (_, cohort, a, _) = await SeedRunAsync(db, StageRotationMode.SingleService);
        PlaceRunIn(db, cohort, a);
        await db.SaveChangesAsync();
        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        var handler = new DeleteStageSlotCommandHandler(db);
        var result = await handler.Handle(new DeleteStageSlotCommand(300), default);

        result.IsFailure.Should().BeTrue("slot 300 holds the third cell of a run that is published");
        result.Error.Code.Should().Be("Schedule.SlotPublished");
    }
}
