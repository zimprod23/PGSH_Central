using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Who a service holds at one moment, on the call shape the macro plan does not make:
/// « auto-répartir ce stage » — every partition, every période, in one go.
///
/// <para>The crossover means only one partition is free in any given column: every other cell is
/// refused because the group is already placed in another stage over the same window. The service
/// queue was nevertheless built over <i>all</i> the call's cohorts and indexed by each cohort's
/// position in the whole ordered list. Partitions are contiguous in that ordering and each service
/// owns a contiguous run of the queue — so an entire partition dropped inside one service's run.</para>
///
/// <para>⚠ Measured on the real base (5MED Psychiatrie 2025-2026, 2026-08-18): all nine columns went
/// to a single service, 69 to 85 students against a capacity of 20, while two of the five allowed
/// services were never used all year. The run reported 60 cells written and no failure — the
/// conflicts it counted are the ones the crossover is made of, so nothing distinguished it from a
/// correct plan.</para>
/// </summary>
public class RotationColumnBalanceTests
{
    private const int Target  = TestHarness.StageId;
    private const int Blocker = 2;
    private const int Columns = 9;
    private const int Groups  = 60;
    private const int PerGroup = 12;

    private static readonly int[] ServiceIds = [61, 62, 63, 64, 91];
    private static readonly string[] Labels = ["A", "B", "C", "D", "E", "F", "G", "H", "I"];

    /// <summary>The column a group's partition occupies in the target stage — every other column of
    /// its year is spent in some other stage, which is what the crossover is.</summary>
    private static int ColumnOf(int groupNumber) => ((groupNumber - 1) % Columns) + 1;

    private static (DateOnly Start, DateOnly End) Window(int period) =>
        (new DateOnly(2025, 11, 1).AddDays((period - 1) * 30),
         new DateOnly(2025, 11, 1).AddDays((period * 30) - 1));

    /// <summary>
    /// Sixty rosters cut into nine partitions, a stage of five services over nine columns, and a
    /// second stage holding every group in every column but its own — the state the whole-stage
    /// arrange actually runs against.
    /// </summary>
    private static void SeedYear(ApplicationDbContext db, bool withCrossover = true)
    {
        var target = db.SeedCatalog();
        foreach (int id in ServiceIds)
            target.AllowedServices.Add(db.SeedService(id, $"Psychiatrie {id}"));

        var blocker = new Stage
        {
            Id = Blocker, Name = "Autres stages", LevelId = TestHarness.LevelId,
            Level = target.Level, Coefficient = 1,
        };
        db.Stages.Add(blocker);

        for (int period = 1; period <= Columns; period++)
        {
            var (start, end) = Window(period);
            db.SeedSlot(target,  period,       period, start, end);
            db.SeedSlot(blocker, 100 + period, period, start, end);
        }

        int cellId = 1;
        for (int number = 1; number <= Groups; number++)
        {
            var group = db.SeedGroup(number, number, Labels[(number - 1) % Labels.Length]);

            var cohort = db.SeedCohortFor(target, group, number);
            for (int s = 0; s < PerGroup; s++)
            {
                var registration = db.SeedRegistration($"E{number}", $"S{s}", group);
                db.InternshipAssignments.Add(new InternshipAssignment
                {
                    Id = Guid.NewGuid(), RegistrationId = registration.Id, Cohort = cohort,
                });
            }

            // Where the group spends the rest of its year. The service is immaterial — the guard
            // reads the window, not the placement — but the cells must exist, because they are the
            // only reason a column of the target stage holds one partition instead of sixty.
            var elsewhere = db.SeedCohortFor(blocker, group, 1000 + number);
            for (int period = 1; period <= Columns && withCrossover; period++)
            {
                if (period == ColumnOf(number)) continue;

                db.CohortSlotAssignments.Add(new CohortSlotAssignment
                {
                    Id = cellId++, CohortId = elsewhere.Id, Cohort = elsewhere,
                    StageSlotId = 100 + period, ServiceId = ServiceIds[0],
                });
            }
        }
    }

    /// <summary>Cohorts per service, per column of the target stage.</summary>
    private static async Task<Dictionary<int, Dictionary<int, int>>> ByColumnAsync(ApplicationDbContext db) =>
        (await db.CohortSlotAssignments
            .Include(a => a.StageSlot)
            .Where(a => a.StageSlot.StageId == Target)
            .ToListAsync())
        .GroupBy(a => a.StageSlot.PeriodNumber)
        .ToDictionary(
            g => g.Key,
            g => g.GroupBy(a => a.ServiceId).ToDictionary(s => s.Key, s => s.Count()));

    [Fact]
    public async Task Arranging_a_whole_stage_spreads_each_column_over_every_service()
    {
        await using var db = TestHarness.NewContext("column-balance-whole-stage");
        SeedYear(db);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(Target, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Assigned.Should().Be(Groups, "the crossover leaves each group exactly one free column");

        var byColumn = await ByColumnAsync(db);
        byColumn.Should().HaveCount(Columns);

        foreach (var (period, byService) in byColumn)
        {
            byService.Should().HaveCount(ServiceIds.Length,
                $"column {period} holds 6 or 7 groups and there are 5 services — none of them may sit empty");

            byService.Values.Max().Should().Be(2,
                $"7 groups over 5 services is 2, 2, 1, 1, 1 — column {period} put " +
                $"{byService.Values.Max()} in one service");
        }
    }

    [Fact]
    public async Task No_service_carries_the_remainder_of_every_column()
    {
        // A column's shape cannot be improved on — seven groups over five services is 2,2,1,1,1
        // whichever way they fall, since the column indexes every queue position exactly once. What
        // is decidable is *which* services carry the pair, and a stable tie-break gave it to the same
        // two leading services in all nine columns: over capacity in every période of the year while
        // the rest sat at half.
        await using var db = TestHarness.NewContext("column-balance-remainder");
        SeedYear(db);
        await db.SaveChangesAsync();

        await db.Arranger().ArrangeAsync(Target, TestHarness.CurrentYearId, null, null, null, default);

        var overTheYear = (await ByColumnAsync(db))
            .SelectMany(c => c.Value)
            .GroupBy(s => s.Key)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Value));

        overTheYear.Should().HaveCount(ServiceIds.Length);
        (overTheYear.Values.Max() - overTheYear.Values.Min()).Should().BeLessThanOrEqualTo(2,
            "60 cells over 5 services is 12 each, and 15 remainders over 9 columns cannot all land "
            + "on the same service");
    }

    [Fact]
    public async Task A_column_is_balanced_on_who_stands_in_it_not_on_who_the_call_reaches()
    {
        // The mechanism, isolated from the arithmetic: the two are the same call, differing only in
        // whether the other stages are there to take the groups out of eight columns each. Balanced
        // over the call, the wider one collapses onto a single service per column; balanced over the
        // column, both give the same shape.
        await using var db = TestHarness.NewContext("column-balance-mechanism");
        SeedYear(db);
        await db.SaveChangesAsync();

        await db.Arranger().ArrangeAsync(Target, TestHarness.CurrentYearId, null, null, null, default);
        var wholeStage = await ByColumnAsync(db);

        await using var scoped = TestHarness.NewContext("column-balance-mechanism-scoped");
        SeedYear(scoped);
        await scoped.SaveChangesAsync();

        // Column 1 alone, targeted the way the macro plan targets it — the call shape that was
        // always correct, and the one the whole-stage run must now agree with.
        await scoped.Arranger().ArrangeAsync(Target, TestHarness.CurrentYearId, ["A"], [1], null, default);

        var byService = (await ByColumnAsync(scoped))[1];

        wholeStage[1].Values.OrderDescending().Should().Equal(byService.Values.OrderDescending(),
            "one partition in one column is one partition in one column, however the call was scoped");
    }

    [Fact]
    public async Task A_stage_nothing_has_crossed_into_refuses_an_unscoped_arrange()
    {
        // Nothing else holds these groups, so no cell is refused and the run would write every
        // (cohort × column): the whole promotion in one stage for the whole year, silently, after
        // which every stage arranged next gets nothing because everyone is busy everywhere. Med6 sits
        // in exactly this state — six stages, ten columns, zero cells — so whichever button is
        // pressed first would decide the year.
        await using var db = TestHarness.NewContext("column-balance-uncrossed");
        SeedYear(db, withCrossover: false);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(Target, TestHarness.CurrentYearId, null, null, null, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.StageWouldFillEveryColumn");

        (await db.CohortSlotAssignments.CountAsync(a => a.StageSlot.StageId == Target))
            .Should().Be(0, "a refusal writes nothing");
    }

    [Fact]
    public async Task Refusing_does_not_destroy_the_cells_already_there()
    {
        // ⚠ The guard sits before the scoped removal, not after it. Decided afterwards, the run would
        // delete the stage's existing cells and then decline to write replacements — the failure mode
        // that once made re-running an arrange silently destroy a good plan.
        await using var db = TestHarness.NewContext("column-balance-uncrossed-keeps");
        SeedYear(db, withCrossover: false);

        var service = await db.Services.FindAsync(ServiceIds[0]);
        int id = 90_000;
        foreach (var cohort in db.Cohorts.Local.Where(c => c.StageId == Target).ToList())
            db.SeedSlotAssignment(id++, cohort, db.StageSlots.Local.First(s => s.StageId == Target), service!);

        await db.SaveChangesAsync();
        int before = await db.CohortSlotAssignments.CountAsync(a => a.StageSlot.StageId == Target);

        var result = await db.Arranger().ArrangeAsync(Target, TestHarness.CurrentYearId, null, null, null, default);

        result.IsFailure.Should().BeTrue();
        (await db.CohortSlotAssignments.CountAsync(a => a.StageSlot.StageId == Target))
            .Should().Be(before, "the refusal must leave the grid exactly as it found it");
    }

    [Fact]
    public async Task A_scoped_arrange_is_how_an_empty_axis_is_filled()
    {
        // The control. The guard must refuse the call that decides the year by accident and nothing
        // else: authoring the crossover one partition and one window at a time — what the rotation
        // block and the macro plan both do — is exactly the supported path onto an empty axis.
        await using var db = TestHarness.NewContext("column-balance-uncrossed-scoped");
        SeedYear(db, withCrossover: false);
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            Target, TestHarness.CurrentYearId, ["A"], [1], null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Assigned.Should().Be(7, "partition A holds 7 rosters and was given one column");
    }

    [Fact]
    public async Task A_single_service_stage_keeps_its_whole_run_in_one_service()
    {
        // Column-wise balancing must not reach into the mode that exists to defeat it: under
        // SingleService the group stands in one service for every column of the run, and the
        // publisher collapses those cells into one période with one evaluation.
        await using var db = TestHarness.NewContext("column-balance-single-service");
        SeedYear(db);
        (await db.Stages.FindAsync(Target))!.RotationMode = StageRotationMode.SingleService;
        await db.SaveChangesAsync();

        // A run of three columns for the three partitions that are free in them — the shape
        // Gynécologie has in the 5th year (k=3, L=3).
        var result = await db.Arranger().ArrangeAsync(
            Target, TestHarness.CurrentYearId, ["A", "B", "C"], [1, 2, 3], null, default);

        result.IsSuccess.Should().BeTrue();

        var byCohort = (await db.CohortSlotAssignments
                .Include(a => a.StageSlot)
                .Where(a => a.StageSlot.StageId == Target)
                .ToListAsync())
            .GroupBy(a => a.CohortId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.ServiceId).Distinct().Count());

        byCohort.Values.Should().AllBeEquivalentTo(1, "one run is one service, whatever the column holds");
    }
}
