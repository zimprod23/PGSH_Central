using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// What a published cell protects, and from which acts.
///
/// <para>⚠ <b>Two holes, both latent until the 3ᵉ MED was published on 13/09/2026</b> (1 000 cellules,
/// 7 464 périodes). Before that the base held no grid-linked période at all, so every one of these
/// paths was unreachable and the whole suite ran in the world where they were safe.</para>
///
/// <list type="number">
/// <item><b>Moving a column had no guard at all.</b> <c>DeleteStageSlot</c> refused a published column;
/// <c>UpdateStageSlot</c> rewrote its dates with no check — and moving is the worse of the two, because
/// deleting fails loudly while moving succeeds and desynchronises in silence: the créneau takes its new
/// dates and the périodes published from it keep their old ones.</item>
/// <item><b>« Set » and « clear » asked different questions of the same cell.</b> Setting refused when
/// the <i>cohorte</i> was published; clearing refused only when <i>that cell</i> was. On a published
/// cohorte you could therefore remove an unpublished cell and not put it back.</item>
/// </list>
/// </summary>
public class PublishedGridGuardTests
{
    private const int ServiceA = 10;
    private const int ServiceB = 11;
    private const int SlotP1 = 1;
    private const int SlotP2 = 2;
    private const int CohortId = 101;

    private static readonly DateOnly P1Start = new(2025, 11, 3);
    private static readonly DateOnly P1End = new(2025, 11, 28);
    private static readonly DateOnly P2Start = new(2025, 12, 1);
    private static readonly DateOnly P2End = new(2025, 12, 19);

    /// <summary>
    /// One cohorte, two columns. P1 carries a cell; P2 carries one too. Publication, when asked for, is
    /// written the way the publisher writes it — a période plus its coverage row, which is the only
    /// honest answer to « is this cell published ».
    /// </summary>
    private static async Task<Cohort> SeedAsync(ApplicationDbContext db, bool published)
    {
        var stage = db.SeedCatalog();
        stage.AllowedServices.Add(db.SeedService(ServiceA, "Service A"));
        stage.AllowedServices.Add(db.SeedService(ServiceB, "Service B"));

        var cohort = db.SeedCohortFor(stage, db.SeedGroup(1, 1), CohortId);
        var slot1 = db.SeedSlot(stage, SlotP1, 1, P1Start, P1End);
        var slot2 = db.SeedSlot(stage, SlotP2, 2, P2Start, P2End);

        var cellP1 = db.SeedSlotAssignment(1, cohort, slot1, db.Services.Local.First(s => s.Id == ServiceA));
        db.SeedSlotAssignment(2, cohort, slot2, db.Services.Local.First(s => s.Id == ServiceA));

        var registration = db.SeedRegistration("Amina", "Benali", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        if (published)
        {
            var period = db.SeedPeriod(assignment, db.Services.Local.First(s => s.Id == ServiceA),
                P1Start, P1End, started: false);
            period.CohortSlotAssignmentId = cellP1.Id;
            db.SeedCoverage(period, cellP1);
        }

        await db.SaveChangesAsync();
        return cohort;
    }

    // ─── ① Moving a published column ──────────────────────────────────────────

    [Fact]
    public async Task A_published_column_cannot_be_moved()
    {
        await using var db = TestHarness.NewContext(nameof(A_published_column_cannot_be_moved));
        await SeedAsync(db, published: true);

        // ⚠ Moved **backwards**, into a window that collides with nothing. Moved forward it would
        // overlap P2, and the overlap guard would refuse it for a different reason entirely — the test
        // would then pass with the publication guard deleted. Measured: it did.
        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP1, TestHarness.StageId, "P1",
                P1Start.AddDays(-7), P1End.AddDays(-7)),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.SlotPublishedCannotMove",
            "the only thing wrong with this move is that the column is published");

        var slot = await db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP1);
        slot.StartDate.Should().Be(P1Start, "a refused move must not have written the dates on its way out");
        slot.EndDate.Should().Be(P1End);
    }

    /// <summary>
    /// The control, and the one that matters: an <b>unpublished</b> column still moves. Without it the
    /// test above would pass on a handler that refused every move.
    /// </summary>
    [Fact]
    public async Task An_unpublished_column_still_moves()
    {
        await using var db = TestHarness.NewContext(nameof(An_unpublished_column_still_moves));
        await SeedAsync(db, published: false);

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP1, TestHarness.StageId, "P1",
                P1Start.AddDays(1), P1End.AddDays(1)),
            default);

        result.IsSuccess.Should().BeTrue();
        (await db.StageSlots.AsNoTracking().SingleAsync(s => s.Id == SlotP1))
            .StartDate.Should().Be(P1Start.AddDays(1));
    }

    /// <summary>
    /// ⚠ The column that is published is P1; P2 carries a cell but no période. Moving P2 must still
    /// work — the guard is about <i>this</i> column, not about the stage having any publication at all.
    /// </summary>
    [Fact]
    public async Task A_sibling_column_of_the_same_stage_still_moves()
    {
        await using var db = TestHarness.NewContext(nameof(A_sibling_column_of_the_same_stage_still_moves));
        await SeedAsync(db, published: true);

        var result = await db.UpdateSlotHandler().Handle(
            new UpdateStageSlotCommand(SlotP2, TestHarness.StageId, "P2",
                P2Start.AddDays(1), P2End.AddDays(1)),
            default);

        result.IsSuccess.Should().BeTrue();
    }

    // ─── ② Set and clear ask the same question ────────────────────────────────

    /// <summary>
    /// The asymmetry. Clearing P2 — a cell with no période behind it — used to succeed on a published
    /// cohorte, while setting it back was refused: a hole nobody could fill without unpublishing the
    /// whole cohorte and destroying everything else with it.
    /// </summary>
    [Fact]
    public async Task An_unpublished_cell_of_a_published_cohort_cannot_be_cleared_either()
    {
        await using var db = TestHarness.NewContext(nameof(An_unpublished_cell_of_a_published_cohort_cannot_be_cleared_either));
        await SeedAsync(db, published: true);

        var cleared = await db.ClearCellHandler().Handle(
            new ClearCohortSlotAssignmentCommand(CohortId, SlotP2), default);

        cleared.IsFailure.Should().BeTrue();
        cleared.Error.Code.Should().Be("Schedule.AlreadyPublished");

        (await db.CohortSlotAssignments.CountAsync(a => a.StageSlotId == SlotP2))
            .Should().Be(1, "the cell somebody could not have put back is still there");
    }

    /// <summary>The other half of the pair, stated so the two cannot drift apart again.</summary>
    [Fact]
    public async Task And_setting_it_is_refused_for_the_same_reason()
    {
        await using var db = TestHarness.NewContext(nameof(And_setting_it_is_refused_for_the_same_reason));
        await SeedAsync(db, published: true);

        var set = await db.SetCellHandler().Handle(
            new SetCohortSlotAssignmentCommand(CohortId, SlotP2, ServiceB), default);

        set.IsFailure.Should().BeTrue();
        set.Error.Code.Should().Be("Schedule.AlreadyPublished");
    }

    /// <summary>
    /// The control for both: on a cohorte nobody published, set and clear work as they always did.
    /// </summary>
    [Fact]
    public async Task On_an_unpublished_cohort_set_and_clear_both_work()
    {
        await using var db = TestHarness.NewContext(nameof(On_an_unpublished_cohort_set_and_clear_both_work));
        await SeedAsync(db, published: false);

        var set = await db.SetCellHandler().Handle(
            new SetCohortSlotAssignmentCommand(CohortId, SlotP2, ServiceB), default);
        set.IsSuccess.Should().BeTrue();

        (await db.CohortSlotAssignments.AsNoTracking().SingleAsync(a => a.StageSlotId == SlotP2))
            .ServiceId.Should().Be(ServiceB);

        var cleared = await db.ClearCellHandler().Handle(
            new ClearCohortSlotAssignmentCommand(CohortId, SlotP2), default);
        cleared.IsSuccess.Should().BeTrue();

        (await db.CohortSlotAssignments.CountAsync(a => a.StageSlotId == SlotP2)).Should().Be(0);
    }
}
