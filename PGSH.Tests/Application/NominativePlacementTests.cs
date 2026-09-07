using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A placement a human chose, and a service held for the people he chose.
/// </summary>
/// <remarks>
/// <para>The defect these close is silent in both directions. <c>RotationArranger</c> deleted and
/// rewrote every unpublished cell in its reach, so a nominative placement — « ces volontaires à
/// Kénitra » — was destroyed by the next « auto-répartir ce stage » with no refusal, no count, and an
/// <c>Assigned = N</c> that looked entirely normal. And « ces services leur sont réservés » could not
/// be said at all: a service has to be in the stage's allowed list for a pin to be accepted, and being
/// in that list is exactly what put it back in the pool.</para>
///
/// <para>⚠ The second half would not have been caught by an occupancy assertion either. Saturation is
/// computed <b>after</b> <c>SaveChangesAsync</c>, as a report; the placement weights by capacity and
/// never reads live occupancy, so the arranger stacks cohorts onto a service somebody else fills and
/// only says so afterwards.</para>
/// </remarks>
public class NominativePlacementTests
{
    private const int Rotation1 = 10;
    private const int Rotation2 = 11;
    private const int Reserved = 12;

    /// <summary>Two ordinary services, one held aside, four rosters, two periods.</summary>
    private static (Stage Stage, Service Held) Seed(ApplicationDbContext db, bool reserve)
    {
        var stage = db.SeedCatalog();
        var s1 = db.SeedService(Rotation1, "Cardiologie A");
        var s2 = db.SeedService(Rotation2, "Cardiologie B");
        var held = db.SeedService(Reserved, "GST Kénitra — Médecine");

        db.AllowInOrder(stage, s1, s2, held);
        if (reserve)
            db.Reserve(stage, held);

        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));
        db.SeedSlot(stage, 2, 2, new DateOnly(2025, 11, 26), new DateOnly(2025, 12, 16));

        for (int n = 1; n <= 4; n++)
            db.SeedCohortFor(stage, db.SeedGroup(n, n), 100 + n);

        return (stage, held);
    }

    [Fact]
    public async Task A_pinned_cell_survives_the_arrange_that_rewrites_everything_around_it()
    {
        await using var db = TestHarness.NewContext(nameof(A_pinned_cell_survives_the_arrange_that_rewrites_everything_around_it));
        var (stage, held) = Seed(db, reserve: true);
        await db.SaveChangesAsync();

        var roster = await db.Cohorts.FirstAsync(c => c.Id == 101);
        var slot = await db.StageSlots.FirstAsync(s => s.PeriodNumber == 1);
        db.SeedSlotAssignment(500, roster, slot, held, CellSource.Pinned);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();

        var pinned = await db.CohortSlotAssignments.FirstOrDefaultAsync(a => a.Id == 500);
        pinned.Should().NotBeNull("a cell somebody chose is not the arranger's to delete");
        pinned!.ServiceId.Should().Be(Reserved);
        pinned.Source.Should().Be(CellSource.Pinned);
    }

    [Fact]
    public async Task The_kept_cells_are_counted_because_a_run_that_writes_fewer_is_otherwise_a_run_that_failed()
    {
        await using var db = TestHarness.NewContext(nameof(The_kept_cells_are_counted_because_a_run_that_writes_fewer_is_otherwise_a_run_that_failed));
        var (_, held) = Seed(db, reserve: true);
        await db.SaveChangesAsync();

        var roster = await db.Cohorts.FirstAsync(c => c.Id == 101);
        var slots = await db.StageSlots.OrderBy(s => s.PeriodNumber).ToListAsync();
        db.SeedSlotAssignment(500, roster, slots[0], held, CellSource.Pinned);
        db.SeedSlotAssignment(501, roster, slots[1], held, CellSource.Pinned);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.Value.PinnedCellsKept.Should().Be(2);

        // 4 rosters × 2 periods = 8 cells, of which 2 belong to somebody else's decision.
        result.Value.Assigned.Should().Be(6);
    }

    [Fact]
    public async Task An_arranged_cell_is_still_rewritten_so_nothing_of_the_old_behaviour_changed()
    {
        await using var db = TestHarness.NewContext(nameof(An_arranged_cell_is_still_rewritten_so_nothing_of_the_old_behaviour_changed));
        var (_, held) = Seed(db, reserve: false);
        await db.SaveChangesAsync();

        var roster = await db.Cohorts.FirstAsync(c => c.Id == 101);
        var slot = await db.StageSlots.FirstAsync(s => s.PeriodNumber == 1);

        // Same cell, same service — but written by the rotation rather than chosen. It is stale.
        db.SeedSlotAssignment(500, roster, slot, held);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.Value.PinnedCellsKept.Should().Be(0);
        (await db.CohortSlotAssignments.AnyAsync(a => a.Id == 500)).Should().BeFalse(
            "an Arranged cell is the rotation's own previous answer, and re-arranging replaces it");
    }

    [Fact]
    public async Task A_reserved_service_never_receives_a_cohort_from_the_rotation()
    {
        await using var db = TestHarness.NewContext(nameof(A_reserved_service_never_receives_a_cohort_from_the_rotation));
        Seed(db, reserve: true);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReservedServices.Should().Be(1);

        (await db.CohortSlotAssignments.AnyAsync(a => a.ServiceId == Reserved))
            .Should().BeFalse("the whole point of the mode is that only a pin puts anybody there");
    }

    [Fact]
    public async Task Without_the_mode_the_same_service_is_filled_by_the_rotation()
    {
        // The control. A refusal assertion that passes on a stage the arranger never reaches proves
        // nothing, so the same fixture is run with the mode left alone.
        await using var db = TestHarness.NewContext(nameof(Without_the_mode_the_same_service_is_filled_by_the_rotation));
        Seed(db, reserve: false);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.Value.ReservedServices.Should().Be(0);
        (await db.CohortSlotAssignments.AnyAsync(a => a.ServiceId == Reserved)).Should().BeTrue();
    }

    [Fact]
    public async Task Reserving_every_service_refuses_by_name_rather_than_as_a_missing_quota()
    {
        await using var db = TestHarness.NewContext(nameof(Reserving_every_service_refuses_by_name_rather_than_as_a_missing_quota));
        var stage = db.SeedCatalog();
        var only = db.SeedService(Reserved, "GST Kénitra — Médecine");
        db.AllowInOrder(stage, only);
        db.Reserve(stage, only);

        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));
        db.SeedCohortFor(stage, db.SeedGroup(1, 1), 101);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsFailure.Should().BeTrue();

        // ⚠ Not NoServicesAdmitLevel: « aucun ne vous accueille » sends the operator to widen quotas
        // that were never the obstacle.
        result.Error.Code.Should().Be("Schedule.AllServicesReserved");
        result.Error.Description.Should().Contain("réservés");
    }

    [Fact]
    public async Task The_capacity_a_reservation_withholds_leaves_the_ceiling_with_it()
    {
        await using var db = TestHarness.NewContext(nameof(The_capacity_a_reservation_withholds_leaves_the_ceiling_with_it));
        Seed(db, reserve: true);
        await db.SaveChangesAsync();

        var reserved = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        await using var open = TestHarness.NewContext(nameof(The_capacity_a_reservation_withholds_leaves_the_ceiling_with_it) + "-open");
        Seed(open, reserve: false);
        await open.SaveChangesAsync();

        var unreserved = await open.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        // ⚠ Deliberate, and the reason ReservedServices is reported beside it: « il manque N places »
        // is measured against a smaller ceiling, and a promotion losing places in silence is the
        // defect the count exists to prevent.
        reserved.Value.TotalCapacity.Should().BeLessThan(unreserved.Value.TotalCapacity);
    }
}
