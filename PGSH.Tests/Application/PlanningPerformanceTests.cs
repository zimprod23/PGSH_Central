using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Cohorts.GetByStage;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Schedule;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// What had to change once a promotion really was a hundred rosters, and what must not change with it.
///
/// <para>Three acts were reported slow on the live base, and each turned out to be a correctness
/// question wearing a performance complaint:</para>
/// <list type="bullet">
///   <item><b>The planning grid</b> shipped every cohorte and every cell in one object — a thousand
///   cells on the current year's biggest stage. Paging the rows is the fix, and the danger it creates
///   is that every count on the screen would then describe 25 rows instead of the selection.</item>
///   <item><b>« Publier tout »</b> refused on the first over-capacity cell, which on a stage-wide
///   publish meant one refusal per cohorte and no statement of the scale. One pass, one refusal.</item>
///   <item><b>« Générer le plan »</b> read the eligible registrations once per cohorte — ~700 round
///   trips for one press. One read, keyed on <b>(roster, niveau) together</b>, which is the part a
///   batch can silently get wrong.</item>
/// </list>
/// </summary>
public class PlanningPerformanceTests
{
    private const int ServiceId    = 1;
    private const int SecondSvcId  = 2;
    private const int OtherLevelId = 77;

    private static readonly DateOnly P1Start = new(2026, 3, 1);
    private static readonly DateOnly P1End   = new(2026, 3, 31);
    private static readonly DateOnly P2Start = new(2026, 4, 1);
    private static readonly DateOnly P2End   = new(2026, 4, 30);

    private static SchedulePublisher Publisher(ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db));

    private static GetStageScheduleQueryHandler GridHandler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db), new ServiceOccupancyCalculator(db),
            new ServiceIntakeCalculator(db));

    // ── The grid: paged rows, whole-selection numbers ────────────────────────

    /// <summary>
    /// Twelve rosters over two columns, cut in two partitions, each cohorte holding two students —
    /// small enough to assert exactly, shaped like the real thing.
    /// </summary>
    private static async Task<Stage> SeedPromotionGridAsync(ApplicationDbContext db, int rosters = 12)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var second  = db.SeedService(SecondSvcId, "Réanimation");
        var p1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 200, 2, P2Start, P2End);

        for (int i = 1; i <= rosters; i++)
        {
            var group = db.SeedGroup(i, i, rotationGroup: i % 2 == 1 ? "A" : "B");
            var cohort = db.SeedCohortFor(stage, group, i);

            for (int s = 0; s < 2; s++)
                db.SeedAssignment(db.SeedRegistration($"E{i}-{s}", "Test", group), cohort);

            // Partition A occupies P1, partition B occupies P2 — the crossover, in miniature.
            db.SeedSlotAssignment(i, cohort, i % 2 == 1 ? p1 : p2, i % 2 == 1 ? service : second);
        }

        await db.SaveChangesAsync();
        return stage;
    }

    [Fact]
    public async Task The_grid_returns_one_page_of_rows_while_counting_the_whole_selection()
    {
        await using var db = TestHarness.NewContext("grid-paged");
        await SeedPromotionGridAsync(db);

        var result = await GridHandler(db).Handle(
            new GetStageScheduleQuery(TestHarness.StageId, PageNumber: 1, PageSize: 5), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cohorts.Items.Should().HaveCount(5, "the page is what is rendered");
        result.Value.Cohorts.TotalCount.Should().Be(12);
        result.Value.Summary.TotalCohorts.Should().Be(12, "the summary describes the selection, not the page");
        result.Value.Summary.ConfiguredUnpublishedCohorts.Should().Be(
            12, "every roster holds a cell and none is published — this is the number « Publier tout » acts on");
    }

    /// <summary>
    /// The trap paging creates: « Publier tout (N) » is fired at the whole selection, so an N counted
    /// from the visible rows would promise 5 and publish 12.
    /// </summary>
    [Fact]
    public async Task The_publishable_count_is_not_the_page_size()
    {
        await using var db = TestHarness.NewContext("grid-publishable-count");
        await SeedPromotionGridAsync(db);

        var page = await GridHandler(db).Handle(
            new GetStageScheduleQuery(TestHarness.StageId, PageNumber: 2, PageSize: 5), default);

        page.Value.Cohorts.Items.Should().HaveCount(5);
        page.Value.Summary.ConfiguredUnpublishedCohorts.Should().Be(12);
        page.Value.Cohorts.Items.Select(c => c.CohortId)
            .Should().NotIntersectWith([1, 2, 3, 4, 5], "page 2 is not page 1");
    }

    [Fact]
    public async Task Filtering_by_partition_narrows_the_rows_but_never_the_partition_list()
    {
        await using var db = TestHarness.NewContext("grid-partition-filter");
        await SeedPromotionGridAsync(db);

        var result = await GridHandler(db).Handle(
            new GetStageScheduleQuery(TestHarness.StageId, RotationGroup: "A", PageSize: 50), default);

        result.Value.Cohorts.Items.Should().OnlyContain(c => c.RotationGroup == "A");
        result.Value.Summary.TotalCohorts.Should().Be(6);
        // The chips are what the user filters *with*: narrowed by the active filter there would be
        // no way back to B.
        result.Value.Summary.Partitions.Select(p => p.Label).Should().BeEquivalentTo(["A", "B"]);
        result.Value.Summary.Partitions.Should().OnlyContain(p => p.CohortCount == 6);
    }

    /// <summary>
    /// « Nouveaux créneaux uniquement » and the « la partition B est déjà là » warning both ask about
    /// rows the filter has removed. Read off the page they answer from five cohortes, which is how a
    /// column already arranged gets quietly rewritten.
    /// </summary>
    [Fact]
    public async Task The_occupied_columns_and_the_partition_usage_survive_paging_and_filtering()
    {
        await using var db = TestHarness.NewContext("grid-usage");
        await SeedPromotionGridAsync(db);

        var result = await GridHandler(db).Handle(
            new GetStageScheduleQuery(TestHarness.StageId, RotationGroup: "A", PageNumber: 1, PageSize: 2),
            default);

        result.Value.Cohorts.Items.Should().HaveCount(2);
        result.Value.Summary.OccupiedSlotIds.Should().BeEquivalentTo(
            [100], "partition A stands in P1 only — and all six of its rosters do, not just the two on screen");

        // The whole stage, deliberately: B is in P2, and that is exactly what the filtered rows
        // cannot say.
        result.Value.Summary.PartitionUsage.Should().Contain(u => u.RotationGroup == "B" && u.StageSlotId == 200);
    }

    [Fact]
    public async Task The_grid_reports_a_saturated_pair_once_however_many_cohorts_stand_in_it()
    {
        await using var db = TestHarness.NewContext("grid-saturation");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 4;
        var p1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);

        for (int i = 1; i <= 3; i++)
        {
            var group = db.SeedGroup(i, i);
            var cohort = db.SeedCohortFor(stage, group, i);
            for (int s = 0; s < 3; s++)
                db.SeedAssignment(db.SeedRegistration($"E{i}-{s}", "T", group), cohort);
            db.SeedSlotAssignment(i, cohort, p1, service);
        }
        await db.SaveChangesAsync();

        var result = await GridHandler(db).Handle(
            new GetStageScheduleQuery(TestHarness.StageId, PageSize: 1), default);

        // Nine students in a service of four, across three cohortes — one problem, not three, and it
        // is visible from a page holding a single row.
        result.Value.Summary.SaturatedCellCount.Should().Be(1);
        var breach = result.Value.Summary.Saturations.Single();
        breach.OccupiedSeats.Should().Be(9);
        breach.Capacity.Should().Be(4);
        breach.Reason.Should().Be(SaturationReason.Total);
    }

    /// <summary>
    /// A service carrying quotas that do not name this promotion refuses it outright, and the grid has
    /// to say so before publish does — it is the one breach « autoriser le dépassement » cannot lift.
    /// </summary>
    [Fact]
    public async Task A_service_that_refuses_the_promotion_is_reported_as_refused_not_as_full()
    {
        await using var db = TestHarness.NewContext("grid-refused");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        db.SeedLevelCapacity(service, OtherLevelId, 10);   // quotas exist, none for this level
        var p1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var group = db.SeedGroup(1, 1);
        var cohort = db.SeedCohortFor(stage, group, 1);
        db.SeedAssignment(db.SeedRegistration("E", "T", group), cohort);
        db.SeedSlotAssignment(1, cohort, p1, service);
        await db.SaveChangesAsync();

        var result = await GridHandler(db).Handle(
            new GetStageScheduleQuery(TestHarness.StageId), default);

        result.Value.Summary.Saturations.Single().Reason.Should().Be(SaturationReason.Refused);
        result.Value.Cohorts.Items.Single().Cells.Single()!.AdmitsLevel.Should().BeFalse();
    }

    // ── Publishing: one refusal, not one per cohorte ─────────────────────────

    /// <summary>
    /// Two services, both over their capacity in the same publish. It used to stop at the first, so a
    /// stage-wide publish was fixed one service at a time — and the screen that published cohorte by
    /// cohorte turned it into a red toast per cohorte.
    /// </summary>
    [Fact]
    public async Task A_stage_publish_names_every_breach_in_one_refusal()
    {
        await using var db = TestHarness.NewContext("publish-aggregate");
        var stage = db.SeedCatalog();
        var first  = db.SeedService(ServiceId, "Cardiologie");
        var second = db.SeedService(SecondSvcId, "Réanimation");
        first.Capacity = 2;
        second.Capacity = 2;
        var p1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 200, 2, P2Start, P2End);

        for (int i = 1; i <= 2; i++)
        {
            var group = db.SeedGroup(i, i);
            var cohort = db.SeedCohortFor(stage, group, i);
            for (int s = 0; s < 4; s++)
                db.SeedAssignment(db.SeedRegistration($"E{i}-{s}", "T", group), cohort);
            db.SeedSlotAssignment(i, cohort, i == 1 ? p1 : p2, i == 1 ? first : second);
        }
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null,
            allowOverCapacity: false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.PublishRefusedByIntake");
        result.Error.Description.Should().Contain("2 affectation(s)");
        result.Error.Description.Should().Contain("Cardiologie");
        result.Error.Description.Should().Contain("Réanimation");

        // ⚠ The guard runs before the write, and the refusal has to leave the base untouched — a
        // handler test that only asserts the failure cannot tell a pre-check from a post-check.
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// One cell in trouble keeps its own sentence. The aggregate exists for a promotion; wrapping a
    /// single breach in « 1 affectation dépasse… » would say less than the message it replaced.
    /// </summary>
    [Fact]
    public async Task A_single_breach_still_refuses_with_its_own_specific_error()
    {
        await using var db = TestHarness.NewContext("publish-single-breach");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 2;
        var group = db.SeedGroup(1, 1);
        var cohort = db.SeedCohortFor(stage, group, 1);
        for (int s = 0; s < 4; s++)
            db.SeedAssignment(db.SeedRegistration($"E{s}", "T", group), cohort);
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null,
            allowOverCapacity: false, default);

        result.Error.Code.Should().Be("Schedule.CapacityExceeded");
    }

    /// <summary>
    /// The half no checkbox lifts has to survive the aggregation, and it has to be visible in it: the
    /// user ticking « autoriser le dépassement » needs to read that some of these refusals will still
    /// stand.
    /// </summary>
    [Fact]
    public async Task An_inadmissible_service_is_named_separately_inside_the_aggregate()
    {
        await using var db = TestHarness.NewContext("publish-aggregate-hard");
        var stage = db.SeedCatalog();
        var open  = db.SeedService(ServiceId, "Cardiologie");
        var shut  = db.SeedService(SecondSvcId, "Réanimation");
        open.Capacity = 2;
        db.SeedLevelCapacity(shut, OtherLevelId, 10);   // takes another promotion, not this one
        var p1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 200, 2, P2Start, P2End);

        for (int i = 1; i <= 2; i++)
        {
            var group = db.SeedGroup(i, i);
            var cohort = db.SeedCohortFor(stage, group, i);
            for (int s = 0; s < 4; s++)
                db.SeedAssignment(db.SeedRegistration($"E{i}-{s}", "T", group), cohort);
            db.SeedSlotAssignment(i, cohort, i == 1 ? p1 : p2, i == 1 ? open : shut);
        }
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null,
            allowOverCapacity: false, default);

        result.Error.Code.Should().Be("Schedule.PublishRefusedByIntake");
        result.Error.Description.Should().Contain("ne peut pas être forcé");
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// …and with the override on, the admissibility refusal is the only one left — which is the whole
    /// reason the two halves were split. Still one refusal, still nothing written.
    /// </summary>
    [Fact]
    public async Task The_override_lifts_the_numbers_and_leaves_the_admissibility_refusal_standing()
    {
        await using var db = TestHarness.NewContext("publish-override-hard");
        var stage = db.SeedCatalog();
        var shut = db.SeedService(ServiceId, "Réanimation");
        db.SeedLevelCapacity(shut, OtherLevelId, 10);
        var group = db.SeedGroup(1, 1);
        var cohort = db.SeedCohortFor(stage, group, 1);
        db.SeedAssignment(db.SeedRegistration("E", "T", group), cohort);
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), shut);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null,
            allowOverCapacity: true, default);

        result.Error.Code.Should().Be("Schedule.LevelNotAdmitted");
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
    }

    // ── Affectation: one read for every cohorte, and the pair kept intact ────

    /// <summary>
    /// The batch's real risk. The candidates are now fetched for every roster and every level of the
    /// call at once, then keyed on <b>(roster, niveau) together</b>. Keyed on either alone — the shape
    /// two independent <c>Any</c>s produce, and the one that turned 833 students into 2 127 — a
    /// student registered in this roster at another level would be affected to a stage he does not owe.
    /// </summary>
    [Fact]
    public async Task Affectation_keeps_the_roster_and_level_pair_together()
    {
        await using var db = TestHarness.NewContext("affect-pair");
        var stage = db.SeedCatalog();
        var group = db.SeedGroup(1, 1);
        var cohort = db.SeedCohortFor(stage, group, 1);

        db.SeedRegistration("Mine", "Level", group);
        // Same roster, another promotion. It exists in the real base: a roster is (year, level,
        // number), but a registration carries its own level and the two only agree by construction.
        db.SeedRegistration("Other", "Level", group, levelId: OtherLevelId);
        await db.SaveChangesAsync();

        var result = await new StudentAffectationService(db).AssignByStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, default);

        result.SuccessCount.Should().Be(1);
        var affected = await db.InternshipAssignments.Include(a => a.Registration).SingleAsync();
        affected.Registration.LevelId.Should().Be(TestHarness.LevelId);
        _ = cohort;
    }

    /// <summary>Several cohortes in one call, each getting its own roster's students and nobody else's.</summary>
    [Fact]
    public async Task Affectation_reads_every_cohorts_candidates_in_one_pass()
    {
        await using var db = TestHarness.NewContext("affect-batch");
        var stage = db.SeedCatalog();

        for (int i = 1; i <= 4; i++)
        {
            var group = db.SeedGroup(i, i);
            db.SeedCohortFor(stage, group, i);
            for (int s = 0; s < 3; s++)
                db.SeedRegistration($"E{i}-{s}", "T", group);
        }
        await db.SaveChangesAsync();

        var result = await new StudentAffectationService(db).AssignByStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, default);

        result.SuccessCount.Should().Be(12);
        var byCohort = await db.InternshipAssignments
            .GroupBy(a => a.CurrentCohortId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        byCohort.Should().HaveCount(4);
        byCohort.Should().OnlyContain(x => x.Count == 3, "no roster's students leaked into another's cohorte");
    }

    /// <summary>A withdrawn registration is still not a candidate — the batch must not widen the rule.</summary>
    [Fact]
    public async Task Affectation_still_skips_a_withdrawn_registration()
    {
        await using var db = TestHarness.NewContext("affect-withdrawn");
        var stage = db.SeedCatalog();
        var group = db.SeedGroup(1, 1);
        db.SeedCohortFor(stage, group, 1);
        db.SeedRegistration("Present", "T", group);
        var gone = db.SeedRegistration("Withdrawn", "T", group);
        gone.Status = RegistrationStatus.Withdrawn;
        await db.SaveChangesAsync();

        var result = await new StudentAffectationService(db).AssignByStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, default);

        result.SuccessCount.Should().Be(1);
    }

    // ── The cohort list carries its own columns ──────────────────────────────

    /// <summary>
    /// « Démarrer / clôturer sur P4-P6 » needs to know which cohortes run in those columns. It used to
    /// read that off the planning grid, which only worked while the grid shipped every cohorte and
    /// every cell — so the fact moved onto the cohorte itself.
    /// </summary>
    [Fact]
    public async Task The_cohort_list_carries_the_periods_each_cohort_stands_in()
    {
        await using var db = TestHarness.NewContext("cohort-periods");
        await SeedPromotionGridAsync(db, rosters: 4);

        var result = await new GetCohortByStageIdQueryHandler(db).Handle(
            new GetCohortsByStageQuery(TestHarness.StageId, TestHarness.CurrentYearId, PageSize: 50), default);

        result.IsSuccess.Should().BeTrue();
        // Odd rosters are partition A in P1, even ones partition B in P2 — see SeedPromotionGridAsync.
        result.Value.Items.Single(c => c.Id == 1).PeriodNumbers.Should().BeEquivalentTo([1]);
        result.Value.Items.Single(c => c.Id == 2).PeriodNumbers.Should().BeEquivalentTo([2]);
    }
}
