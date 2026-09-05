using FluentAssertions;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Schedule;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using PGSH.SharedKernel;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// What the planning grid says about itself: which cells are published, and what an empty table
/// means.
///
/// <para>Two defects, both of which read as data rather than as a bug. ⚠ <b>Publication was marked
/// per cohorte and never per cell</b>, and the obvious per-cell reading —
/// <c>ServicePeriod.CohortSlotAssignmentId</c> — names only the <i>first</i> cell of a run: measured
/// on Gynécologie Obstétrique 2026-2027, 363 cells of which the FK names 121, so 242 published cells
/// would have shown as free. ⚠ And <b>an empty grid had three causes wearing one blank</b>: from
/// 2017-2018 to 2025-2026 the base holds 105 626 périodes for 0 créneau, because the Access import
/// carried the rotations that were served and the source had no grid to carry — so a past year shows
/// an empty table while every dossier shows its périodes, and an admin reading « rien n'est réparti »
/// can go and lay an axis over a year that finished.</para>
/// </summary>
public class StageScheduleGridTests
{
    private const int ServiceId = 1;

    private static readonly DateOnly P1Start = new(2025, 10, 1);
    private static readonly DateOnly P1End   = new(2025, 10, 31);
    private static readonly DateOnly P2Start = new(2025, 11, 1);
    private static readonly DateOnly P2End   = new(2025, 11, 30);

    private static Task<Result<StageScheduleResponse>> GridAsync(
        ApplicationDbContext db, string? rotationGroup = null) =>
        new GetStageScheduleQueryHandler(
                db,
                new AcademicYearResolver(db),
                new ServiceOccupancyCalculator(db),
                new ServiceIntakeCalculator(db))
            .Handle(new GetStageScheduleQuery(TestHarness.StageId, RotationGroup: rotationGroup), default);

    /// <summary>
    /// A run of two columns served as one stay — the shape <c>SchedulePublisher</c> writes for a
    /// <see cref="StageRotationMode.SingleService"/> stage: one période, one coverage row per cell,
    /// and the foreign key on the lead cell alone.
    /// </summary>
    private static async Task SeedPublishedRunAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        stage.RotationMode = StageRotationMode.SingleService;

        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort  = db.SeedCohort(stage, 10, "Groupe 10");

        var p1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 101, 2, P2Start, P2End);

        var lead     = db.SeedSlotAssignment(1000, cohort, p1, service);
        var trailing = db.SeedSlotAssignment(1001, cohort, p2, service);

        var assignment = db.SeedAssignment(
            db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup), cohort);
        var period = db.SeedPeriod(assignment, service, P1Start, P2End, started: false);

        db.SeedCoverage(period, lead);
        db.SeedCoverage(period, trailing, leadCell: false);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Every_cell_a_period_covers_is_published_including_the_ones_no_foreign_key_names()
    {
        await using var db = TestHarness.NewContext("grid-published-run");
        await SeedPublishedRunAsync(db);

        var result = await GridAsync(db);

        result.IsSuccess.Should().BeTrue();
        var cells = result.Value.Cohorts.Items.Single().Cells;
        cells.Should().HaveCount(2);
        cells.Should().AllSatisfy(c => c!.IsPublished.Should().BeTrue(
            "a run's trailing cells carry a coverage row even though the foreign key names only the "
            + "first — read from the key they would show as free and be rewritten by the next arrange"));
    }

    [Fact]
    public async Task A_cell_no_period_covers_is_not_published()
    {
        await using var db = TestHarness.NewContext("grid-unpublished-cell");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var slot = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        db.SeedSlotAssignment(1000, cohort, slot, service);
        await db.SaveChangesAsync();

        var result = await GridAsync(db);

        result.Value.Cohorts.Items.Single().Cells.Single()!.IsPublished.Should().BeFalse();
        result.Value.Summary.EmptyGridNote.Should().BeNull("the grid holds a cell to show");
    }

    [Fact]
    public async Task An_empty_grid_on_an_imported_year_says_the_periods_came_from_no_grid()
    {
        await using var db = TestHarness.NewContext("grid-imported-year");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        // What the Access import produced: the rotation that was served, hanging off no cell at all.
        var assignment = db.SeedAssignment(
            db.SeedRegistration("Ali", "Amrani", cohort.AcademicGroup), cohort);
        db.SeedPeriod(assignment, service, P1Start, P1End, started: false);
        await db.SaveChangesAsync();

        var summary = (await GridAsync(db)).Value.Summary;

        summary.DeclaredSlotCount.Should().Be(0);
        summary.ServedPeriodCount.Should().Be(1);
        summary.EmptyGridNote.Should().Contain("historique importé");
    }

    [Fact]
    public async Task An_empty_grid_with_nothing_served_asks_for_an_axis()
    {
        await using var db = TestHarness.NewContext("grid-no-axis");
        var stage = db.SeedCatalog();
        db.SeedCohort(stage, 10, "Groupe 10");
        await db.SaveChangesAsync();

        var summary = (await GridAsync(db)).Value.Summary;

        summary.ServedPeriodCount.Should().Be(0, "the question was put and the answer is none");
        summary.EmptyGridNote.Should().Contain("axe");
        summary.EmptyGridNote.Should().NotContain("historique",
            "nothing was served here, so there is no history to explain the blank");
    }

    [Fact]
    public async Task An_axis_over_a_promotion_with_no_cohorte_does_not_ask_for_an_arrange()
    {
        await using var db = TestHarness.NewContext("grid-no-cohorts");
        var stage = db.SeedCatalog();
        db.SeedSlot(stage, 100, 1, P1Start, P1End);
        await db.SaveChangesAsync();

        var summary = (await GridAsync(db)).Value.Summary;

        summary.DeclaredSlotCount.Should().Be(1);
        summary.ServedPeriodCount.Should().BeNull("the axis exists, so the question was never put");
        summary.EmptyGridNote.Should().Contain("cohorte",
            "arranging is the second gesture; this promotion has not had the first");
    }

    [Fact]
    public async Task An_axis_nobody_is_arranged_into_asks_for_the_arrange()
    {
        await using var db = TestHarness.NewContext("grid-not-arranged");
        var stage = db.SeedCatalog();
        db.SeedCohort(stage, 10, "Groupe 10");
        db.SeedSlot(stage, 100, 1, P1Start, P1End);
        await db.SaveChangesAsync();

        var summary = (await GridAsync(db)).Value.Summary;

        summary.DeclaredSlotCount.Should().Be(1);
        summary.EmptyGridNote.Should().Contain("répartition");
    }

    /// <summary>
    /// ⚠ The note describes the stage, never the current filter. Told « aucune cohorte » on a
    /// partition that simply has not been arranged yet, an admin goes and undoes a cut that is right.
    /// </summary>
    [Fact]
    public async Task The_note_is_silent_when_the_stage_holds_cells_the_filter_hides()
    {
        await using var db = TestHarness.NewContext("grid-filtered");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var slot = db.SeedSlot(stage, 100, 1, P1Start, P1End);

        var arranged = db.SeedGroup(10, 1, rotationGroup: "A");
        var idle     = db.SeedGroup(20, 2, rotationGroup: "B");
        var placed   = db.SeedCohortFor(stage, arranged, 10);
        db.SeedCohortFor(stage, idle, 20);
        db.SeedSlotAssignment(1000, placed, slot, service);
        await db.SaveChangesAsync();

        var summary = (await GridAsync(db, rotationGroup: "B")).Value.Summary;

        summary.TotalCohorts.Should().Be(1, "the filter narrows the rows");
        summary.EmptyGridNote.Should().BeNull(
            "partition B holds no cell, but the stage does — the emptiness on screen is the filter's");
    }
}
