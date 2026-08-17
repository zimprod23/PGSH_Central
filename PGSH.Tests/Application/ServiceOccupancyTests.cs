using FluentAssertions;
using PGSH.Application.AcademicYears;
using PGSH.Application.Hospitals.Services.Occupancy;
using PGSH.Domain.Hospitals;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;

namespace PGSH.Tests.Application;

/// <summary>
/// What a service actually holds, day by day.
///
/// <para>⚠ <b>A service's load cannot be read one period at a time.</b> Nothing ties two stages'
/// periods together — <c>StageSlot</c> is keyed (stage, year, number), so Chirurgie P1 and ANES REA
/// P1 have independent dates and legitimately different lengths. Print one row per slot and each
/// number is that slot's own cohorts, while the students standing in the service on a given morning
/// are the union of every window covering that day. The peak therefore lives in the overlap, and a
/// per-slot list never shows it. That is the whole reason this timeline is segmented, and the first
/// test below is that claim.</para>
///
/// <para>Measured on the real base while this was written: <b>233 of 353 planned cells are over
/// capacity (66%), worst 85 students against 20</b> — and all 148 services carry the imported
/// default of 20 with not one quota authored. So the over-capacity paths here are the normal case,
/// not the edge.</para>
/// </summary>
public class ServiceOccupancyTests
{
    private static readonly DateOnly YearStart = new(2025, 9, 1);
    private static readonly DateOnly YearEnd   = new(2026, 8, 31);

    // ─── The segmentation itself: pure, no database ─────────────────────────────────────────────

    private static OccupancyPlacement Placement(
        string stage, int levelId, int groupNumber, int students, DateOnly start, DateOnly end) =>
        new(StageId: stage.GetHashCode() & 0x7fffffff, StageName: stage, LevelId: levelId,
            LevelLabel: $"niveau {levelId}", PeriodNumber: 1, CohortId: groupNumber,
            GroupNumber: groupNumber, Students: students, StartDate: start, EndDate: end);

    [Fact]
    public void The_peak_lives_in_the_overlap_that_no_single_period_shows()
    {
        // Chirurgie holds 30 from 1 to 31 March; Médecine holds 32 from 15 March to 15 April. Read
        // period by period the service never exceeds 32. It actually holds 62 for a fortnight.
        var segments = OccupancyTimeline.Build(
        [
            Placement("Chirurgie", 1, 1, 30, new(2026, 3, 1),  new(2026, 3, 31)),
            Placement("Médecine",  2, 2, 32, new(2026, 3, 15), new(2026, 4, 15)),
        ]);

        segments.Should().HaveCount(3);

        segments[0].StartDate.Should().Be(new DateOnly(2026, 3, 1));
        segments[0].EndDate.Should().Be(new DateOnly(2026, 3, 14));
        segments[0].Occupants.Sum(o => o.Students).Should().Be(30);

        segments[1].StartDate.Should().Be(new DateOnly(2026, 3, 15));
        segments[1].EndDate.Should().Be(new DateOnly(2026, 3, 31));
        segments[1].Occupants.Sum(o => o.Students).Should().Be(62, "both stages are there at once");

        segments[2].StartDate.Should().Be(new DateOnly(2026, 4, 1));
        segments[2].EndDate.Should().Be(new DateOnly(2026, 4, 15));
        segments[2].Occupants.Sum(o => o.Students).Should().Be(32);
    }

    [Fact]
    public void Back_to_back_windows_do_not_merge()
    {
        // The boundary is end + 1, not end. Using `end` would make the last day of one window and the
        // first of the next share a boundary, and the two would collapse into one segment reading a
        // load neither of them ever had.
        var segments = OccupancyTimeline.Build(
        [
            Placement("Chirurgie", 1, 1, 30, new(2026, 3, 1),  new(2026, 3, 31)),
            Placement("Chirurgie", 1, 2, 25, new(2026, 4, 1),  new(2026, 4, 30)),
        ]);

        segments.Should().HaveCount(2);
        segments[0].Occupants.Sum(o => o.Students).Should().Be(30);
        segments[1].Occupants.Sum(o => o.Students).Should().Be(25);
    }

    [Fact]
    public void A_stretch_with_nobody_in_it_produces_no_segment()
    {
        // A service empty in December has no December row — not a row reading zero, which would
        // suggest something is planned there.
        var segments = OccupancyTimeline.Build(
        [
            Placement("Chirurgie", 1, 1, 30, new(2025, 11, 1), new(2025, 11, 30)),
            Placement("Chirurgie", 1, 2, 25, new(2026, 1, 5),  new(2026, 1, 31)),
        ]);

        segments.Should().HaveCount(2);
        segments.Should().OnlyContain(s => s.Occupants.Count > 0);
        segments[0].EndDate.Should().Be(new DateOnly(2025, 11, 30));
        segments[1].StartDate.Should().Be(new DateOnly(2026, 1, 5));
    }

    [Fact]
    public void One_window_wholly_inside_another_is_three_segments()
    {
        var segments = OccupancyTimeline.Build(
        [
            Placement("Chirurgie", 1, 1, 10, new(2026, 3, 1),  new(2026, 3, 31)),
            Placement("Médecine",  2, 2, 40, new(2026, 3, 10), new(2026, 3, 20)),
        ]);

        segments.Select(s => s.Occupants.Sum(o => o.Students)).Should().Equal([10, 50, 10]);
    }

    [Fact]
    public void An_empty_service_has_an_empty_timeline()
    {
        OccupancyTimeline.Build([]).Should().BeEmpty();
    }

    // ─── The capacity rule, through the handler ─────────────────────────────────────────────────

    private const int ServiceId  = 7;
    private const int OtherLevel = 60;

    /// <summary>
    /// Two promotions in one service over overlapping windows — the shape every capacity question in
    /// this system is really about.
    /// </summary>
    private static (ApplicationDbContext Db, Service Service) Seed(string name)
    {
        var db = TestHarness.NewContext(name);
        var stage = db.SeedCatalog(YearStart, YearEnd);
        db.SeedLevel(OtherLevel, "6ème année", 6, AcademicProgram.Medecine);
        var otherStage = db.SeedStage(stageId: 60, name: "Urgences", levelId: OtherLevel);

        var service = db.SeedService(ServiceId, "Cardiologie");

        // 3rd year: 12 students, 1–31 March.
        var thirdGroup = db.SeedGroup(groupId: 1, groupNumber: 1);
        var thirdCohort = db.SeedCohortFor(stage, thirdGroup, cohortId: 1);
        var thirdSlot = db.SeedSlot(stage, slotId: 1, periodNumber: 1, new(2026, 3, 1), new(2026, 3, 31));
        db.SeedSlotAssignment(1, thirdCohort, thirdSlot, service);
        for (int i = 0; i < 12; i++)
            db.SeedAssignment(db.SeedRegistration($"T{i}", "Troisieme", thirdGroup), thirdCohort);

        // 6th year: 15 students, 15 March – 15 April.
        var sixthGroup = db.SeedGroup(groupId: 2, groupNumber: 1);
        sixthGroup.LevelId = OtherLevel;
        var sixthCohort = db.SeedCohortFor(otherStage, sixthGroup, cohortId: 2);
        var sixthSlot = db.SeedSlot(otherStage, slotId: 2, periodNumber: 1, new(2026, 3, 15), new(2026, 4, 15));
        db.SeedSlotAssignment(2, sixthCohort, sixthSlot, service);
        for (int i = 0; i < 15; i++)
            db.SeedAssignment(
                db.SeedRegistration($"S{i}", "Sixieme", sixthGroup, levelId: OtherLevel), sixthCohort);

        db.SaveChanges();
        return (db, service);
    }

    private static GetServiceOccupancyQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    [Fact]
    public async Task With_no_quota_the_total_governs_and_both_promotions_count_against_it()
    {
        // ⚠ No ServiceLevelCapacity rows does not mean "unconfigured", it means the service is open
        // to everyone — and then one ceiling is shared by every promotion at once. 12 + 15 = 27
        // against 20 is the overflow, even though neither promotion alone exceeds it.
        var (db, _) = Seed(nameof(With_no_quota_the_total_governs_and_both_promotions_count_against_it));
        await using var _db = db;

        var result = await Handler(db).Handle(new GetServiceOccupancyQuery(ServiceId, null), default);

        result.IsSuccess.Should().BeTrue();
        var report = result.Value;

        report.Rule.Should().Be(CapacityRule.Total);
        report.TotalCapacity.Should().Be(20);
        report.Segments.Should().HaveCount(3);

        var peak = report.Segments.Single(s => s.Students == 27);
        peak.StartDate.Should().Be(new DateOnly(2026, 3, 15));
        peak.EndDate.Should().Be(new DateOnly(2026, 3, 31));
        peak.Capacity.Should().Be(20);
        peak.Overflow.Should().Be(7);
        peak.Levels.Should().HaveCount(2);
        peak.Levels.Should().OnlyContain(l => l.Capacity == null, "an open service has no per-promotion ceiling");

        report.Segments.Where(s => s.Students != 27).Should().OnlyContain(s => s.Overflow == 0);
        report.Summary.PeakStudents.Should().Be(27);
        report.Summary.OverCapacitySegments.Should().Be(1);
        report.Summary.DaysOverCapacity.Should().Be(17);
        report.Summary.DistinctLevels.Should().Be(2);
    }

    [Fact]
    public async Task With_quotas_each_promotion_is_measured_against_its_own_and_the_total_is_ignored()
    {
        // Quotas replace Service.Capacity rather than sitting under it: 10 + 15 on a service of 20
        // holds 25 and only the 3rd year (12 against 10) is in breach.
        var (db, service) = Seed(nameof(With_quotas_each_promotion_is_measured_against_its_own_and_the_total_is_ignored));
        await using var _db = db;

        db.SeedLevelCapacity(service, TestHarness.LevelId, capacity: 10);
        db.SeedLevelCapacity(service, OtherLevel, capacity: 15);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(new GetServiceOccupancyQuery(ServiceId, null), default)).Value;

        report.Rule.Should().Be(CapacityRule.PerLevel);
        var peak = report.Segments.Single(s => s.Students == 27);

        peak.Capacity.Should().BeNull("there is no single ceiling on a restricted service");
        peak.Overflow.Should().Be(2, "12 against 10 for the 3rd year; the 6th year's 15 is exactly its quota");

        peak.Levels.Single(l => l.LevelId == TestHarness.LevelId)
            .Should().BeEquivalentTo(new { Students = 12, Capacity = 10, Overflow = 2, NotAdmitted = false });
        peak.Levels.Single(l => l.LevelId == OtherLevel)
            .Should().BeEquivalentTo(new { Students = 15, Capacity = 15, Overflow = 0, NotAdmitted = false });
    }

    [Fact]
    public async Task A_promotion_with_no_quota_on_a_restricted_service_is_not_admitted_at_all()
    {
        // ⚠ The *first* quota closes the service to every promotion without one. That is a different
        // fault from being over a quota — the students should not be there at all — and it has to
        // read differently, because the fix is different: author a row, don't raise a number.
        var (db, service) = Seed(nameof(A_promotion_with_no_quota_on_a_restricted_service_is_not_admitted_at_all));
        await using var _db = db;

        db.SeedLevelCapacity(service, OtherLevel, capacity: 15);
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(new GetServiceOccupancyQuery(ServiceId, null), default)).Value;

        var thirdYear = report.Segments
            .SelectMany(s => s.Levels)
            .Where(l => l.LevelId == TestHarness.LevelId)
            .ToList();

        thirdYear.Should().NotBeEmpty();
        thirdYear.Should().OnlyContain(l => l.NotAdmitted && l.Capacity == 0);
        thirdYear.Should().OnlyContain(l => l.Overflow == l.Students, "none of them may be here");

        report.Segments.SelectMany(s => s.Levels)
            .Where(l => l.LevelId == OtherLevel)
            .Should().OnlyContain(l => !l.NotAdmitted);
    }

    [Fact]
    public async Task The_occupants_of_a_segment_name_their_stage_promotion_and_groups()
    {
        var (db, _) = Seed(nameof(The_occupants_of_a_segment_name_their_stage_promotion_and_groups));
        await using var _db = db;

        var report = (await Handler(db).Handle(new GetServiceOccupancyQuery(ServiceId, null), default)).Value;
        var peak = report.Segments.Single(s => s.Students == 27);

        peak.Occupants.Should().HaveCount(2);
        peak.Occupants.Select(o => o.StageName).Should().BeEquivalentTo(["Cardiologie", "Urgences"]);
        peak.Occupants.Should().OnlyContain(o => o.GroupNumbers == "1");
        peak.Occupants.Sum(o => o.Students).Should().Be(27);
        report.Summary.DistinctStages.Should().Be(2, "two stages of two promotions share this service");
    }

    [Fact]
    public async Task A_service_nobody_is_planned_into_reports_an_empty_timeline_not_a_failure()
    {
        // "Nothing planned here" and "this service does not exist" are different answers, and an
        // empty list is the honest one for the first.
        var (db, _) = Seed(nameof(A_service_nobody_is_planned_into_reports_an_empty_timeline_not_a_failure));
        await using var _db = db;
        db.SeedService(99, "Néphrologie");
        await db.SaveChangesAsync();

        var report = (await Handler(db).Handle(new GetServiceOccupancyQuery(99, null), default)).Value;

        report.Segments.Should().BeEmpty();
        report.Summary.PeakStudents.Should().Be(0);
        report.Summary.PeakStart.Should().BeNull();
        report.Summary.OverCapacitySegments.Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_service_is_a_not_found_rather_than_an_empty_report()
    {
        var (db, _) = Seed(nameof(An_unknown_service_is_a_not_found_rather_than_an_empty_report));
        await using var _db = db;

        var result = await Handler(db).Handle(new GetServiceOccupancyQuery(4242, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Services.NotFound");
    }
}
