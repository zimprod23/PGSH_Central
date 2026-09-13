using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A cell the arranger may not touch still occupies the service it sits in.
///
/// <para>⚠ <b>It did not, until 13/09/2026.</b> Locked cells — published, or pinned by a human — are
/// excluded from the column the arranger balances, which is right: they are not its to place. But they
/// were also excluded from the <em>capacity</em> that balance is computed against, so the free cohortes
/// were spread over the services as though those services were empty. A service already holding a
/// published cohorte therefore received its full proportional share of free ones <b>on top</b>, and
/// nothing in the result said so.</para>
///
/// <para>⚠ <b>Latent for as long as the base published nothing, and then it was not.</b> The faculty
/// planned and published the 3ᵉ MED of 2026-2027 on 13/09/2026 — 1 000 cellules, 7 464 périodes — so
/// re-arranging any column of it would have piled free cohortes onto services its published cells were
/// already filling. That is the day a documented « harmless today » expired.</para>
/// </summary>
public class ArrangerPublishedLoadTests
{
    private const int ServiceA = 10;
    private const int ServiceB = 11;

    /// <summary>
    /// One column, four rosters of ten, two services of twenty. Balanced, that is two cohortes each —
    /// and exactly one service's worth of room disappears for every two cohortes locked into it.
    /// </summary>
    private static Stage SeedOneColumn(ApplicationDbContext db, int rosters = 4)
    {
        var stage = db.SeedCatalog();
        stage.AllowedServices.Add(db.SeedService(ServiceA, "Service A"));
        stage.AllowedServices.Add(db.SeedService(ServiceB, "Service B"));

        db.SeedSlot(stage, slotId: 1, periodNumber: 1,
            start: new DateOnly(2025, 11, 3), end: new DateOnly(2025, 11, 28));

        for (int n = 1; n <= rosters; n++)
        {
            var cohort = db.SeedCohortFor(stage, db.SeedGroup(n, n), 100 + n);
            // Ten students apiece: two cohortes fill a service of twenty exactly.
            for (int i = 0; i < 10; i++)
                db.SeedAssignment(db.SeedRegistration($"E{n}{i}", $"N{n}{i}", cohort.AcademicGroup), cohort);
        }

        return stage;
    }

    private static async Task<Dictionary<int, int>> PlacementsAsync(ApplicationDbContext db) =>
        await db.CohortSlotAssignments
            .AsNoTracking()
            .GroupBy(a => a.ServiceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

    /// <summary>
    /// The control: nothing locked, so the column balances two and two. Without this the test below
    /// would also pass on an arranger that simply refused to use Service A at all.
    /// </summary>
    [Fact]
    public async Task With_nothing_locked_the_column_balances_evenly()
    {
        await using var db = TestHarness.NewContext(nameof(With_nothing_locked_the_column_balances_evenly));
        SeedOneColumn(db);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        var placed = await PlacementsAsync(db);

        placed[ServiceA].Should().Be(2);
        placed[ServiceB].Should().Be(2);
    }

    /// <summary>
    /// The defect. Two cohortes are pinned into Service A — twenty students, its whole number — so the
    /// two free ones have to go to Service B. Before the fix they were split one and one, leaving
    /// Service A with three cohortes (thirty students against a capacity of twenty) and a result that
    /// reported a perfectly ordinary <c>Assigned</c>.
    /// </summary>
    [Fact]
    public async Task A_service_already_full_of_locked_cells_takes_no_more()
    {
        await using var db = TestHarness.NewContext(nameof(A_service_already_full_of_locked_cells_takes_no_more));
        SeedOneColumn(db);

        foreach (int cohortId in new[] { 101, 102 })
            db.CohortSlotAssignments.Add(new CohortSlotAssignment
            {
                CohortId = cohortId, StageSlotId = 1, ServiceId = ServiceA,
                Source = CellSource.Pinned,
            });

        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PinnedCellsKept.Should().Be(2);

        var placed = await PlacementsAsync(db);

        placed[ServiceA].Should().Be(2, "its twenty places are already taken by the two pinned cohortes");
        placed.GetValueOrDefault(ServiceB).Should().Be(2, "the free cohortes go where there is room");
    }

    /// <summary>
    /// Half full: one cohorte pinned into Service A leaves it room for one more, so the three free
    /// ones split one to A and two to B rather than piling on.
    /// </summary>
    [Fact]
    public async Task A_service_half_full_takes_what_it_has_room_for()
    {
        await using var db = TestHarness.NewContext(nameof(A_service_half_full_takes_what_it_has_room_for));
        SeedOneColumn(db);

        db.CohortSlotAssignments.Add(new CohortSlotAssignment
        {
            CohortId = 101, StageSlotId = 1, ServiceId = ServiceA, Source = CellSource.Pinned,
        });
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        var placed = await PlacementsAsync(db);

        placed[ServiceA].Should().Be(2, "one pinned plus the one place it had left");
        placed[ServiceB].Should().Be(2);
    }

    /// <summary>
    /// ⚠ Everything locked into one service, and the free cohortes still have to go somewhere. The
    /// arithmetic must not divide by an all-zero pool — over-capacity is this faculty's normal state,
    /// and a run that threw here would refuse to plan a promotion that is merely full.
    /// </summary>
    [Fact]
    public async Task A_pool_with_no_room_left_still_places_everybody()
    {
        await using var db = TestHarness.NewContext(nameof(A_pool_with_no_room_left_still_places_everybody));
        SeedOneColumn(db, rosters: 6);

        foreach (int cohortId in new[] { 101, 102 })
            db.CohortSlotAssignments.Add(new CohortSlotAssignment
            {
                CohortId = cohortId, StageSlotId = 1, ServiceId = ServiceA, Source = CellSource.Pinned,
            });
        foreach (int cohortId in new[] { 103, 104 })
            db.CohortSlotAssignments.Add(new CohortSlotAssignment
            {
                CohortId = cohortId, StageSlotId = 1, ServiceId = ServiceB, Source = CellSource.Pinned,
            });

        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue("a full promotion is planned over capacity, not refused");
        (await PlacementsAsync(db)).Values.Sum().Should().Be(6, "every cohorte got a cell");
    }
}
