using FluentAssertions;
using PGSH.Application.AcademicYears;
using PGSH.Application.Hospitals.Services.Occupancy;
using PGSH.Application.Hospitals.Services.OccupancyReport;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;

namespace PGSH.Tests.Application;

/// <summary>
/// The cross-service occupancy report — the questions no single service page can answer.
/// </summary>
/// <remarks>
/// ⚠ Every case here is about a number that is <em>wrong in a plausible way</em> if the arithmetic
/// slips: a service reused in three windows summed into one impossible peak, a saturation measured
/// on a filtered load, an empty service silently dropped. Each of those reads as a correct report.
/// </remarks>
public class OccupancyReportTests
{
    private static GetOccupancyReportQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    private static readonly DateOnly P1Start = new(2025, 9, 1);
    private static readonly DateOnly P1End   = new(2025, 10, 31);
    private static readonly DateOnly P2Start = new(2025, 11, 1);
    private static readonly DateOnly P2End   = new(2025, 12, 31);

    /// <summary>
    /// Two cohorts of 10 in one service over one window is 20 standing there at once; the same
    /// cohort passing through twice is still 10.
    ///
    /// <para>⚠ This is the arithmetic the whole report rests on. Summing the placements instead of
    /// reading the timeline turns « 10 étudiants, deux fois » into « 20 à la fois », which is a
    /// saturation that never happened — and it looks exactly like a real one.</para>
    /// </summary>
    [Fact]
    public async Task A_peak_is_simultaneous_presence_never_a_sum_over_the_year()
    {
        using var db = TestHarness.NewContext(nameof(A_peak_is_simultaneous_presence_never_a_sum_over_the_year));

        var stage = db.SeedCatalog();
        var service = db.SeedService(10, "Cardiologie A");
        var cohort = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, cohort, 10);

        var p1 = db.SeedSlot(stage, 101, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 102, 2, P2Start, P2End);
        db.SeedSlotAssignment(1001, cohort, p1, service);
        db.SeedSlotAssignment(1002, cohort, p2, service);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetOccupancyReportQuery(), default);

        result.IsSuccess.Should().BeTrue();

        var row = result.Value.Services.Single(s => s.ServiceId == service.Id);
        row.PeakStudents.Should().Be(10, "one cohort in two consecutive windows is never twenty people");
        row.SegmentCount.Should().Be(2);
        row.Stages.Single().Students.Should().Be(20, "the yearly volume is a different question, and is still asked");
    }

    /// <summary>
    /// A service in scope that holds nobody all year. Invisible from its own page — where it looks
    /// like a service with nothing planned, which is exactly what it is — and it is the other half of
    /// a saturation somewhere else.
    /// </summary>
    [Fact]
    public async Task A_service_nobody_uses_is_counted_rather_than_dropped()
    {
        using var db = TestHarness.NewContext(nameof(A_service_nobody_uses_is_counted_rather_than_dropped));

        var stage = db.SeedCatalog();
        var used = db.SeedService(10, "Cardiologie A");
        db.SeedService(11, "Cardiologie B");

        var cohort = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, cohort, 12);
        var p1 = db.SeedSlot(stage, 101, 1, P1Start, P1End);
        db.SeedSlotAssignment(1001, cohort, p1, used);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetOccupancyReportQuery(), default);

        result.Value.Totals.ServicesInScope.Should().Be(2);
        result.Value.Totals.ServicesNeverUsed.Should().Be(1);
        result.Value.Services.Should().Contain(s => s.ServiceId == 11 && s.SegmentCount == 0);

        result.Value.Notes.Should().Contain(n => n.Contains("n'accueillent personne"));
    }

    /// <summary>
    /// The stage lists two services and puts everybody in one. No service page can state this: the
    /// unused one simply looks quiet.
    /// </summary>
    [Fact]
    public async Task A_stage_that_uses_fewer_services_than_it_may_says_so()
    {
        using var db = TestHarness.NewContext(nameof(A_stage_that_uses_fewer_services_than_it_may_says_so));

        var stage = db.SeedCatalog();
        var a = db.SeedService(10, "Cardiologie A");
        var b = db.SeedService(11, "Cardiologie B");
        stage.AllowedServices.Add(a);
        stage.AllowedServices.Add(b);

        var cohort = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, cohort, 8);
        var p1 = db.SeedSlot(stage, 101, 1, P1Start, P1End);
        db.SeedSlotAssignment(1001, cohort, p1, a);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetOccupancyReportQuery(), default);

        var row = result.Value.Stages.Single();
        row.ServicesAllowed.Should().Be(2);
        row.ServicesUsed.Should().Be(1);
        row.ServicesUnused.Should().Be(1);
        row.HeaviestServiceName.Should().Be("Cardiologie A");
        row.HeaviestServiceLoad.Should().Be(8);
    }

    /// <summary>
    /// ⚠ <b>The filter narrows what is listed and what is attributed — never the load a saturation
    /// is measured on.</b> A service holding 12 third-years and 12 fifth-years against a ceiling of
    /// 20 is over, and it stays over when somebody asks about the fifth year alone. Measuring the
    /// filtered half would print « ok » for a service that refuses the publish.
    /// </summary>
    [Fact]
    public async Task A_promotion_filter_never_lowers_the_saturation_of_a_shared_service()
    {
        using var db = TestHarness.NewContext(nameof(A_promotion_filter_never_lowers_the_saturation_of_a_shared_service));

        var third = db.SeedCatalog();
        db.SeedLevel(5, "5ème année", 5);
        var fifth = db.SeedStage(2, "Gynécologie", levelId: 5);

        var service = db.SeedService(10, "Cardiologie A");

        var thirdCohort = db.SeedCohort(third, 1, "G1");
        SeedStudents(db, thirdCohort, 12);
        var fifthCohort = db.SeedCohortFor(fifth, db.SeedGroup(2, 2), 2);
        SeedStudents(db, fifthCohort, 12);

        var thirdSlot = db.SeedSlot(third, 101, 1, P1Start, P1End);
        var fifthSlot = db.SeedSlot(fifth, 201, 1, P1Start, P1End);
        db.SeedSlotAssignment(1001, thirdCohort, thirdSlot, service);
        db.SeedSlotAssignment(1002, fifthCohort, fifthSlot, service);
        await db.SaveChangesAsync();

        var whole = await Handler(db).Handle(new GetOccupancyReportQuery(), default);
        var justFifth = await Handler(db).Handle(new GetOccupancyReportQuery(LevelId: 5), default);

        whole.Value.Services.Single(s => s.ServiceId == 10).PeakStudents.Should().Be(24);

        var filtered = justFifth.Value.Services.Single(s => s.ServiceId == 10);
        filtered.PeakStudents.Should().Be(24, "the service still holds twenty-four people that morning");
        filtered.Share.Should().Be(12, "twelve of them are the promotion asked about");
        filtered.OverCapacitySegments.Should().Be(1, "over its ceiling of 20 whoever is asking");
    }

    /// <summary>
    /// A service the quotas do not admit is a different fault from one over its number: the first
    /// refusal cannot be forced at publication, the second can.
    /// </summary>
    [Fact]
    public async Task A_promotion_the_quotas_do_not_admit_is_reported_separately()
    {
        using var db = TestHarness.NewContext(nameof(A_promotion_the_quotas_do_not_admit_is_reported_separately));

        var stage = db.SeedCatalog();
        var service = db.SeedService(10, "Cardiologie A");

        // The first quota restricts: from here the service admits no promotion without a row — and
        // this one names a promotion the cohort below does not belong to.
        db.SeedLevel(99, "1ère année Pharmacie", 1, AcademicProgram.Pharmacie);
        db.SeedLevelCapacity(service, levelId: 99, capacity: 30);

        var cohort = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, cohort, 6);
        var p1 = db.SeedSlot(stage, 101, 1, P1Start, P1End);
        db.SeedSlotAssignment(1001, cohort, p1, service);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetOccupancyReportQuery(), default);

        var row = result.Value.Services.Single(s => s.ServiceId == 10);
        row.Rule.Should().Be(CapacityRule.PerLevel);
        row.LevelsNotAdmitted.Should().ContainSingle();
        row.Saturation.Should().BeNull("a ceiling of 0 sorts as the least saturated, which is exactly wrong here");

        result.Value.Totals.ServicesAdmittingNobody.Should().Be(1);
        result.Value.Notes.Should().Contain(n => n.Contains("ne se force pas"));
    }

    /// <summary>
    /// ⚠ An empty report has two causes calling for opposite acts — no créneau authored, or créneaux
    /// nobody is in — and « 0 étudiant » collapses them into a third reading, that the report is
    /// broken. It is also the state of the live base today, so this is the screen the user meets
    /// first.
    /// </summary>
    [Fact]
    public async Task Nothing_planned_is_said_in_words_not_left_as_a_zero()
    {
        using var db = TestHarness.NewContext(nameof(Nothing_planned_is_said_in_words_not_left_as_a_zero));

        db.SeedCatalog();
        db.SeedService(10, "Cardiologie A");
        db.SeedService(11, "Cardiologie B");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetOccupancyReportQuery(), default);

        result.IsSuccess.Should().BeTrue("no planning is a state to describe, not a failure");
        result.Value.Totals.PlacementCount.Should().Be(0);
        result.Value.Notes.Should().Contain(n => n.Contains("Bloc de rotation"));
    }

    /// <summary>
    /// The default capacity every imported service carries is a number nobody wrote, and every
    /// saturation below is measured against it. Same rule as <c>StageCatalogueFigure</c>: say it when
    /// a figure is not authored, stay silent when it is.
    /// </summary>
    [Fact]
    public async Task A_capacity_nobody_authored_is_named_and_only_when_it_is_uniform()
    {
        using var db = TestHarness.NewContext(nameof(A_capacity_nobody_authored_is_named_and_only_when_it_is_uniform));

        db.SeedCatalog();
        db.SeedService(10, "Cardiologie A");
        db.SeedService(11, "Cardiologie B");
        await db.SaveChangesAsync();

        var uniform = await Handler(db).Handle(new GetOccupancyReportQuery(), default);
        uniform.Value.Notes.Should().Contain(n => n.Contains("valeur par défaut de l'import"));

        db.Services.First(s => s.Id == 11).Capacity = 35;
        await db.SaveChangesAsync();

        var authored = await Handler(db).Handle(new GetOccupancyReportQuery(), default);
        authored.Value.Notes.Should().NotContain(n => n.Contains("valeur par défaut de l'import"),
            "a warning that fires whatever the data says is noise, and noise hides the real one");
    }

    /// <summary>
    /// A month's bar is the peak reached inside it, never a mean: an average over a month with one
    /// saturated week reads comfortable, and the week is what somebody has to act on.
    /// </summary>
    [Fact]
    public async Task A_month_carries_the_peak_reached_inside_it()
    {
        using var db = TestHarness.NewContext(nameof(A_month_carries_the_peak_reached_inside_it));

        var stage = db.SeedCatalog();
        var service = db.SeedService(10, "Cardiologie A");

        var big = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, big, 30);
        var small = db.SeedCohortFor(stage, db.SeedGroup(2, 2), 2);
        SeedStudents(db, small, 4);

        // One week of thirty inside a month otherwise holding four.
        db.SeedSlotAssignment(1001, big, db.SeedSlot(stage, 101, 1,
            new DateOnly(2025, 10, 6), new DateOnly(2025, 10, 12)), service);
        db.SeedSlotAssignment(1002, small, db.SeedSlot(stage, 102, 2,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31)), service);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetOccupancyReportQuery(), default);

        var october = result.Value.Months.Single(m => m is { Year: 2025, Month: 10 });
        october.PeakStudents.Should().Be(34, "the week of thirty-four is the month's peak, not its average");
        october.ServicesOverCapacity.Should().Be(1);
    }

    /// <summary>
    /// ⚠ A peak held for months must be reported as the months it lasts, not as the first stretch
    /// that reaches it.
    ///
    /// <para>Found on the real 2026-2027 plan: the 3ᵉ and 4ᵉ année run together from September to
    /// March, so 1 858 students stand in the faculty on every one of those segments. `MaxBy` returned
    /// the first, and the document announced « du 07/09 au 06/10 » — a month — directly under a chart
    /// showing the plateau. The number was right and the window was wrong, which is the readable kind
    /// of wrong.</para>
    /// </summary>
    [Fact]
    public async Task A_sustained_peak_is_reported_as_the_whole_stretch_it_lasts()
    {
        using var db = TestHarness.NewContext(nameof(A_sustained_peak_is_reported_as_the_whole_stretch_it_lasts));

        var stage = db.SeedCatalog();
        var service = db.SeedService(10, "Cardiologie A");

        // Two cohorts sitting in the service over two consecutive windows: the load is the same 20 on
        // both, so the peak spans them and its envelope is the pair, not the first one.
        var a = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, a, 10);
        var b = db.SeedCohortFor(stage, db.SeedGroup(2, 2), 2);
        SeedStudents(db, b, 10);

        var p1 = db.SeedSlot(stage, 101, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, 102, 2, P2Start, P2End);
        db.SeedSlotAssignment(1001, a, p1, service);
        db.SeedSlotAssignment(1002, b, p1, service);
        db.SeedSlotAssignment(1003, a, p2, service);
        db.SeedSlotAssignment(1004, b, p2, service);
        await db.SaveChangesAsync();

        var totals = (await Handler(db).Handle(new GetOccupancyReportQuery(), default)).Value.Totals;

        totals.PeakStudents.Should().Be(20);
        totals.PeakStart.Should().Be(P1Start, "the peak begins on the first day it is reached");
        totals.PeakEnd.Should().Be(P2End, "…and ends on the last, not at the end of the first segment");
        totals.PeakDays.Should().Be(P2End.DayNumber - P1Start.DayNumber + 1);
    }

    /// <summary>
    /// A month's stacked bar has to add up: the split is read off the month's peak <em>segment</em>,
    /// not from each promotion's own peak, which would sum to more than the total because two
    /// promotions do not peak on the same day.
    /// </summary>
    [Fact]
    public async Task A_months_promotion_split_adds_up_to_its_peak()
    {
        using var db = TestHarness.NewContext(nameof(A_months_promotion_split_adds_up_to_its_peak));

        var third = db.SeedCatalog();
        db.SeedLevel(5, "5ème année", 5);
        var fifth = db.SeedStage(2, "Gynécologie", levelId: 5);
        var service = db.SeedService(10, "Cardiologie A");

        var thirdCohort = db.SeedCohort(third, 1, "G1");
        SeedStudents(db, thirdCohort, 12);
        var fifthCohort = db.SeedCohortFor(fifth, db.SeedGroup(2, 2), 2);
        SeedStudents(db, fifthCohort, 7);

        db.SeedSlotAssignment(1001, thirdCohort, db.SeedSlot(third, 101, 1, P1Start, P1End), service);
        db.SeedSlotAssignment(1002, fifthCohort, db.SeedSlot(fifth, 201, 1, P1Start, P1End), service);
        await db.SaveChangesAsync();

        var months = (await Handler(db).Handle(new GetOccupancyReportQuery(), default)).Value.Months;

        months.Should().NotBeEmpty();

        foreach (var month in months)
        {
            month.Levels.Sum(l => l.Students).Should().Be(month.PeakStudents,
                "a stacked bar whose parts do not add up to its total is worse than no bar");
        }
    }

    /// <summary>
    /// ⚠ The planning grid, the arranger's balance and the pre-publish guard all read
    /// <c>ServiceOccupancyLookup</c>. It <b>summed</b> every cell overlapping the asked-for window,
    /// so two cells that each touch the window without touching each other were added together.
    ///
    /// <para>Reported from the screen on 2026-09-03 and reproduced exactly: on Pédiatrie2 the grid
    /// showed <b>118</b> for a window in which the service never held more than <b>62</b> — the two
    /// 4ᵉ année Pédiatrie columns are consecutive (one ends 06/10, the next starts 07/10) and both
    /// merely overlap the pharmaciens' window. The per-service page and the charge report said 62
    /// throughout, because they cut at boundaries.</para>
    /// </summary>
    [Fact]
    public async Task Two_consecutive_columns_are_never_added_together()
    {
        using var db = TestHarness.NewContext(nameof(Two_consecutive_columns_are_never_added_together));

        var stage = db.SeedCatalog();
        var service = db.SeedService(10, "Pédiatrie2");

        // Two cohorts of 10, in the same service, over two *consecutive* windows: 10 at a time.
        var first = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, first, 10);
        var second = db.SeedCohortFor(stage, db.SeedGroup(2, 2), 2);
        SeedStudents(db, second, 10);

        var p1 = db.SeedSlot(stage, 101, 1, new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 6));
        var p2 = db.SeedSlot(stage, 102, 2, new DateOnly(2026, 10, 7), new DateOnly(2026, 11, 6));
        db.SeedSlotAssignment(1001, first, p1, service);
        db.SeedSlotAssignment(1002, second, p2, service);
        await db.SaveChangesAsync();

        var lookup = await new ServiceOccupancyCalculator(db).BuildAsync([service.Id], default);

        // A window straddling both — the shape the pharmaciens' column had.
        lookup.LoadOn(service.Id, new DateOnly(2026, 10, 6), new DateOnly(2026, 11, 3))
            .Should().Be(10, "the two columns are consecutive; they are never in the service together");

        // …and each window on its own is still measured correctly.
        lookup.LoadOn(service.Id, p1.StartDate, p1.EndDate).Should().Be(10);
        lookup.LoadOn(service.Id, p2.StartDate, p2.EndDate).Should().Be(10);
    }

    /// <summary>
    /// The control: cells that genuinely coexist must still add up, or the fix above would have
    /// turned a real overload into a silent pass.
    /// </summary>
    [Fact]
    public async Task Cells_that_really_overlap_are_still_summed()
    {
        using var db = TestHarness.NewContext(nameof(Cells_that_really_overlap_are_still_summed));

        var stage = db.SeedCatalog();
        var service = db.SeedService(10, "Pédiatrie2");

        var a = db.SeedCohort(stage, 1, "G1");
        SeedStudents(db, a, 10);
        var b = db.SeedCohortFor(stage, db.SeedGroup(2, 2), 2);
        SeedStudents(db, b, 12);

        var p1 = db.SeedSlot(stage, 101, 1, new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 6));
        // Starts inside P1 — the two are in the service together for a fortnight.
        var p2 = db.SeedSlot(stage, 102, 2, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 20));
        db.SeedSlotAssignment(1001, a, p1, service);
        db.SeedSlotAssignment(1002, b, p2, service);
        await db.SaveChangesAsync();

        var lookup = await new ServiceOccupancyCalculator(db).BuildAsync([service.Id], default);

        lookup.LoadOn(service.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 20))
            .Should().Be(22, "they overlap for a fortnight, so the ceiling really does carry both");

        // The peak is inside the window even when the window opens before it.
        lookup.LoadOn(service.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 20)).Should().Be(22);
    }

    private static void SeedStudents(ApplicationDbContext db, Cohort cohort, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var registration = db.SeedRegistration(
                levelId: cohort.Stage.LevelId,
                group: cohort.AcademicGroup,
                firstName: $"E{cohort.Id}-{i}",
                lastName: "Test");

            db.SeedAssignment(registration, cohort);
        }
    }
}
