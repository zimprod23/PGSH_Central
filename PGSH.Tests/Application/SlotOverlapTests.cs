using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Two periods of ONE stage may never run at the same time: its cohorts rotate through them in
// sequence. Windows are inclusive of both ends, so touching dates already collide.
//
// Two *different* stages of the same level may share a window — that is how a promotion split into
// partitions is planned (Médecine P1 and Chirurgie P1 on the same dates, A in one and B in the
// other). Double-booking is caught per group instead; see GroupScheduleConflictTests.
public class SlotOverlapTests
{
    private const int SecondStageId = 2;
    private const int OtherLevelId  = 9;
    private const int OtherLevelStageId = 3;

    private static readonly DateOnly MarchStart = new(2026, 3, 1);
    private static readonly DateOnly MarchEnd   = new(2026, 3, 31);

    /// <summary>
    /// One level with two stages (Cardiologie, Pédiatrie) plus a stage on a different level.
    /// Cardiologie already owns March as period 1.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();                       // stage 1, level 1

        db.Stages.Add(new Stage
        {
            Id = SecondStageId, Name = "Pédiatrie", LevelId = TestHarness.LevelId, Coefficient = 1,
        });

        var otherLevel = new Level { Id = OtherLevelId, Label = "4ème année", Year = 4 };
        db.Levels.Add(otherLevel);
        db.Stages.Add(new Stage
        {
            Id = OtherLevelStageId, Name = "Chirurgie", LevelId = OtherLevelId, Coefficient = 1,
        });

        db.SeedSlot(stage, 100, 1, MarchStart, MarchEnd);   // Cardiologie P1 = all of March
        await db.SaveChangesAsync();
    }

    private static CreateStageSlotCommandHandler CreateHandler(ApplicationDbContext db) =>
        new(db, new SlotOverlapGuard(db));

    private static UpdateStageSlotCommandHandler UpdateHandler(ApplicationDbContext db) =>
        new(db, new SlotOverlapGuard(db), new GroupScheduleConflictGuard(db));

    private static CreateStageSlotCommand NewSlot(
        int stageId, int periodNumber, DateOnly start, DateOnly end, int? academicYearId = null) =>
        new(stageId, academicYearId ?? TestHarness.CurrentYearId, periodNumber, null, start, end);

    [Fact]
    public async Task A_period_overlapping_another_period_of_the_same_stage_is_refused()
    {
        await using var db = TestHarness.NewContext("slot-same-stage");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(TestHarness.StageId, 2, new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotOverlap");
        result.Error.Description.Should().Contain("même stage");
    }

    [Fact]
    public async Task Another_stage_of_the_same_level_may_run_over_the_same_dates()
    {
        // The faculty's own layout (example_stage_assignement/Med3.png): Médecine and Chirurgie run
        // the same windows, with partition A in one and B in the other — which is the point of
        // partitioning, since it halves the load on every service. Refusing this made the published
        // planning unrepresentable; the double-booking it was meant to stop is caught per group.
        await using var db = TestHarness.NewContext("slot-cross-stage");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(SecondStageId, 1, MarchStart, MarchEnd), default);

        result.IsSuccess.Should().BeTrue("no group is placed by declaring a period");
    }

    [Fact]
    public async Task A_period_on_a_different_level_may_run_at_the_same_time()
    {
        await using var db = TestHarness.NewContext("slot-other-level");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(OtherLevelStageId, 1, MarchStart, MarchEnd), default);

        result.IsSuccess.Should().BeTrue("4ème année students are not the ones doing the 3ème année stage");
    }

    [Fact]
    public async Task A_period_starting_the_day_after_the_previous_one_ends_is_accepted()
    {
        await using var db = TestHarness.NewContext("slot-consecutive");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(TestHarness.StageId, 2, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_period_starting_the_same_day_the_previous_one_ends_is_refused()
    {
        await using var db = TestHarness.NewContext("slot-touching");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(TestHarness.StageId, 2, MarchEnd, new DateOnly(2026, 4, 30)), default);

        result.IsFailure.Should().BeTrue("31/03 belongs to period 1; period 2 must start on 01/04");
        result.Error.Code.Should().Be("Schedule.SlotOverlap");
    }

    [Theory]
    [InlineData("2026-02-15", "2026-03-05")]   // straddles the start
    [InlineData("2026-03-20", "2026-04-10")]   // straddles the end
    [InlineData("2026-03-10", "2026-03-20")]   // fully inside
    [InlineData("2026-02-01", "2026-04-30")]   // fully contains
    [InlineData("2026-03-01", "2026-03-31")]   // identical
    public async Task Every_shape_of_overlap_is_caught(string start, string end)
    {
        await using var db = TestHarness.NewContext($"slot-shape-{start}");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(TestHarness.StageId, 2, DateOnly.Parse(start), DateOnly.Parse(end)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotOverlap");
    }

    [Fact]
    public async Task A_non_overlapping_period_before_the_existing_one_is_accepted()
    {
        await using var db = TestHarness.NewContext("slot-before");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(TestHarness.StageId, 2, new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 5)), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task The_duplicate_period_number_guard_still_applies()
    {
        await using var db = TestHarness.NewContext("slot-duplicate-number");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(
            NewSlot(TestHarness.StageId, 1, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.DuplicatePeriodNumber(1));
    }

    [Fact]
    public async Task A_refused_period_is_not_persisted()
    {
        await using var db = TestHarness.NewContext("slot-not-saved");
        await SeedAsync(db);

        await CreateHandler(db).Handle(
            NewSlot(TestHarness.StageId, 2, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 20)), default);

        (await db.StageSlots.CountAsync()).Should().Be(1, "only the original March period exists");
    }

    [Fact]
    public async Task Moving_a_period_onto_another_one_is_refused()
    {
        await using var db = TestHarness.NewContext("slot-move-collide");
        await SeedAsync(db);
        var stage = await db.Stages.FirstAsync(s => s.Id == TestHarness.StageId);
        db.SeedSlot(stage, 200, 2, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        await db.SaveChangesAsync();

        // Drag period 2 back into March, where period 1 lives.
        var result = await UpdateHandler(db).Handle(
            new UpdateStageSlotCommand(200, TestHarness.StageId, null,
                new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotOverlap");
    }

    [Fact]
    public async Task Moving_a_period_within_free_space_is_accepted()
    {
        await using var db = TestHarness.NewContext("slot-move-ok");
        await SeedAsync(db);
        var stage = await db.Stages.FirstAsync(s => s.Id == TestHarness.StageId);
        db.SeedSlot(stage, 200, 2, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        await db.SaveChangesAsync();

        var result = await UpdateHandler(db).Handle(
            new UpdateStageSlotCommand(200, TestHarness.StageId, null,
                new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)), default);

        result.IsSuccess.Should().BeTrue();
        (await db.StageSlots.FirstAsync(s => s.Id == 200)).StartDate.Should().Be(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public async Task A_period_never_collides_with_itself_when_only_its_label_changes()
    {
        await using var db = TestHarness.NewContext("slot-move-self");
        await SeedAsync(db);

        var result = await UpdateHandler(db).Handle(
            new UpdateStageSlotCommand(100, TestHarness.StageId, "P1 — Cardiologie", MarchStart, MarchEnd), default);

        result.IsSuccess.Should().BeTrue("the slot being edited is excluded from its own overlap check");
        (await db.StageSlots.FirstAsync(s => s.Id == 100)).Label.Should().Be("P1 — Cardiologie");
    }

    [Fact]
    public async Task Creating_a_period_on_an_unknown_stage_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("slot-unknown-stage");
        await SeedAsync(db);

        var result = await CreateHandler(db).Handle(NewSlot(999, 1, MarchStart, MarchEnd), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NotFound(999));
    }
}
