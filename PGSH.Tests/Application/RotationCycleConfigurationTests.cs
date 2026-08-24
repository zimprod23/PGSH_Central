using System.Text.Json;
using FluentAssertions;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Domain.Audit;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Reopening the rotation-block screen. The configuration has to come back, and it has to come back
/// as it <i>is</i> — the axis on disk — rather than as it was once typed.
/// </summary>
public class RotationCycleConfigurationTests
{
    private const int Gyneco = TestHarness.StageId;
    private const int Neuro  = 2;
    private const int Orl    = 3;

    private static GetRotationCycleQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new PGSH.Application.AcademicYears.AcademicYearResolver(db));

    private static GetRotationCycleQuery Query =>
        new(TestHarness.LevelId, TestHarness.CurrentYearId);

    /// <summary>
    /// A block of three: Gynéco for two columns, Neuro and ORL for one each — T = 4. Every stage
    /// carries a slot on every column, which is what the axis actually looks like on disk.
    /// </summary>
    private static void SeedAxis(ApplicationDbContext db, params int[] stageIds)
    {
        var first = db.SeedCatalog();

        foreach (int id in stageIds.Where(id => id != Gyneco))
        {
            db.Stages.Add(new Stage
            {
                Id = id, Name = $"Stage {id}", LevelId = TestHarness.LevelId,
                Level = first.Level, Coefficient = 1,
            });
        }

        int slotId = 1;
        foreach (int stageId in stageIds)
        {
            var stage = db.Stages.Local.First(s => s.Id == stageId);
            for (int column = 1; column <= 4; column++)
            {
                db.SeedSlot(stage, slotId++, column,
                    new DateOnly(2025, 11, 1).AddDays((column - 1) * 30),
                    new DateOnly(2025, 11, 1).AddDays((column * 30) - 1));
            }
        }
    }

    private static void SeedApply(ApplicationDbContext db, DateTime at, params (int StageId, int Periods)[] stages)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = "ROTATION_CYCLE_APPLIED",
            EntityType = "Level",
            EntityId = TestHarness.LevelId.ToString(),
            CreatedAt = at,
            Metadata = JsonSerializer.Serialize(new
            {
                levelId = TestHarness.LevelId,
                academicYearId = (int?)null,
                stages = stages.Select(s => new { s.StageId, s.Periods }),
                columns = 4,
            }),
        });
    }

    [Fact]
    public async Task The_block_comes_back_with_its_stages_in_the_order_they_were_authored()
    {
        // The order is not decoration: RotationTiling lays the first partition's year out in exactly
        // the order the stages were given, so partition A walks Gynéco P1-2, Neuro P3, ORL P4. A form
        // that reopens with them re-sorted describes a different plan from the one on disk.
        await using var db = TestHarness.NewContext("cycle-config-order");
        SeedAxis(db, Gyneco, Neuro, Orl);
        SeedApply(db, new DateTime(2026, 8, 14, 19, 9, 0, DateTimeKind.Utc),
            (Orl, 1), (Gyneco, 2), (Neuro, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query, default);

        result.IsSuccess.Should().BeTrue();
        var block = result.Value.Blocks.Should().ContainSingle().Subject;

        block.Stages.Select(s => s.StageId).Should().Equal(Orl, Gyneco, Neuro);
        block.Stages.Select(s => s.Periods).Should().Equal(1, 2, 1);
        block.Stages.Should().OnlyContain(s => s.PeriodsSource == RotationPeriodsSource.Authored);
        block.Columns.Should().Be(4);
        block.Windows.Should().HaveCount(4);
        block.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_latest_apply_wins_over_the_one_it_replaced()
    {
        await using var db = TestHarness.NewContext("cycle-config-latest");
        SeedAxis(db, Gyneco, Neuro, Orl);
        SeedApply(db, new DateTime(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc),
            (Gyneco, 1), (Neuro, 2), (Orl, 1));
        SeedApply(db, new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc),
            (Gyneco, 2), (Neuro, 1), (Orl, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query, default);

        result.Value.Blocks.Should().ContainSingle()
            .Which.Stages.Select(s => s.Periods).Should().Equal(2, 1, 1);
    }

    [Fact]
    public async Task Without_an_apply_on_record_the_durations_are_read_off_the_cells()
    {
        // ⚠ The axis alone cannot state kₛ — every stage of a block carries a slot on every column,
        // which is precisely what lets them cross over. What a cohort *holds* can: two cells in
        // Gynéco is two columns there.
        await using var db = TestHarness.NewContext("cycle-config-derived");
        SeedAxis(db, Gyneco, Neuro, Orl);

        var group = db.SeedGroup(1, 1, "A");
        var service = db.SeedService(10, "Service");
        foreach (var (stageId, columns) in new[] { (Gyneco, 2), (Neuro, 1), (Orl, 1) })
        {
            var stage = db.Stages.Local.First(s => s.Id == stageId);
            var cohort = db.SeedCohortFor(stage, group, stageId * 100);
            for (int column = 1; column <= columns; column++)
            {
                db.SeedSlotAssignment(
                    stageId * 1000 + column, cohort,
                    db.StageSlots.Local.First(s => s.StageId == stageId && s.PeriodNumber == column),
                    service);
            }
        }

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query, default);
        var block = result.Value.Blocks.Should().ContainSingle().Subject;

        block.Stages.Single(s => s.StageId == Gyneco).Periods.Should().Be(2);
        block.Stages.Should().OnlyContain(s => s.PeriodsSource == RotationPeriodsSource.Derived);
        block.AppliedAt.Should().BeNull("nothing recorded the apply");
    }

    [Fact]
    public async Task An_axis_with_neither_apply_nor_cells_says_so_rather_than_inventing_a_duration()
    {
        // Med6's state today: ten columns authored, nothing arranged. « 1 période » returned here is a
        // placeholder for a form field, not a claim about the block — same reason OutcomeSource and
        // CnpnSource exist.
        await using var db = TestHarness.NewContext("cycle-config-unknown");
        SeedAxis(db, Gyneco, Neuro);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query, default);

        result.Value.Blocks.Should().ContainSingle()
            .Which.Stages.Should().OnlyContain(s => s.PeriodsSource == RotationPeriodsSource.Unknown);
    }

    [Fact]
    public async Task Stages_whose_windows_no_longer_match_are_a_different_block()
    {
        // A block is defined by sharing an axis, so a stage whose dates were nudged on its own grid
        // has genuinely left it. Reporting it as still aligned is the drift PeriodAxisDiagnostics
        // exists to surface, not something to paper over here.
        await using var db = TestHarness.NewContext("cycle-config-drift");
        SeedAxis(db, Gyneco, Neuro, Orl);

        var moved = db.StageSlots.Local.First(s => s.StageId == Orl && s.PeriodNumber == 1);
        moved.StartDate = moved.StartDate.AddDays(3);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query, default);

        result.Value.Blocks.Should().HaveCount(2);
        result.Value.Blocks[0].Stages.Select(s => s.StageId).Should().BeEquivalentTo([Gyneco, Neuro]);
        result.Value.Blocks[1].Stages.Should().ContainSingle().Which.StageId.Should().Be(Orl);
    }

    [Fact]
    public async Task A_promotion_with_no_axis_yet_returns_no_block_rather_than_failing()
    {
        await using var db = TestHarness.NewContext("cycle-config-empty");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Blocks.Should().BeEmpty();
        result.Value.LevelLabel.Should().Be("3ème année");
    }

    [Fact]
    public async Task An_unreadable_audit_entry_costs_the_prefill_and_not_the_request()
    {
        await using var db = TestHarness.NewContext("cycle-config-bad-json");
        SeedAxis(db, Gyneco, Neuro);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), Action = "ROTATION_CYCLE_APPLIED", EntityType = "Level",
            EntityId = TestHarness.LevelId.ToString(), CreatedAt = DateTime.UtcNow,
            Metadata = "{ not json",
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Blocks.Should().ContainSingle()
            .Which.Stages.Should().OnlyContain(s => s.PeriodsSource == RotationPeriodsSource.Unknown);
    }
}
