using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Publishing turns the planning grid into execution records: one ServicePeriod per
// (student × grid cell). It is the point of no return, so it refuses to run twice, refuses an
// unconfigured grid, and refuses to over-book a service beyond its capacity — counting occupancy
// globally, since the same physical service may host two stages over overlapping dates.
public class SchedulePublishTests
{
    private const int ServiceId  = 1;
    private const int SecondSvcId = 2;
    private const int CohortId   = 10;

    private static readonly DateOnly P1Start = new(2026, 3, 1);
    private static readonly DateOnly P1End   = new(2026, 3, 31);
    private static readonly DateOnly P2Start = new(2026, 4, 1);
    private static readonly DateOnly P2End   = new(2026, 4, 30);

    private static SchedulePublisher Publisher(ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db));

    /// <summary>A cohort of <paramref name="students"/> routed through one slot in one service.</summary>
    private static async Task<Cohort> SeedGridAsync(
        ApplicationDbContext db, int students, int capacity = 20, bool withSlotAssignment = true)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = capacity;

        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        var slot = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        if (withSlotAssignment)
            db.SeedSlotAssignment(1, cohort, slot, service);

        for (int i = 0; i < students; i++)
        {
            var registration = db.SeedRegistration($"Etudiant{i}", "Test", cohort.AcademicGroup);
            db.SeedAssignment(registration, cohort);
        }

        await db.SaveChangesAsync();
        return cohort;
    }

    [Fact]
    public async Task Publishing_creates_one_period_per_student_and_grid_cell()
    {
        await using var db = TestHarness.NewContext("publish-basic");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var second = db.SeedService(SecondSvcId, "Réanimation");
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);
        db.SeedSlotAssignment(2, cohort, db.SeedSlot(stage, 200, 2, P2Start, P2End), second);
        for (int i = 0; i < 4; i++)
            db.SeedAssignment(db.SeedRegistration($"E{i}", "Test", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsSuccess.Should().BeTrue();
        (await db.ServicePeriods.CountAsync()).Should().Be(8, "4 students × 2 grid cells");
    }

    [Fact]
    public async Task Published_periods_carry_the_slot_window_and_start_inactive()
    {
        await using var db = TestHarness.NewContext("publish-shape");
        await SeedGridAsync(db, students: 2);

        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        var periods = await db.ServicePeriods.ToListAsync();
        periods.Should().OnlyContain(p => p.StartDate == P1Start && p.EndDate == P1End);
        periods.Should().OnlyContain(p => p.CohortSlotAssignmentId == 1);
        periods.Should().OnlyContain(p => !p.IsStarted && !p.IsComplete,
            "publishing plans the rotation; an admin starts it later");
    }

    [Fact]
    public async Task Publishing_twice_is_refused()
    {
        await using var db = TestHarness.NewContext("publish-twice");
        await SeedGridAsync(db, students: 2);
        (await Publisher(db).PublishCohortAsync(CohortId, false, default)).IsSuccess.Should().BeTrue();

        var result = await Publisher(db).PublishCohortAsync(CohortId, false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.ScheduleAlreadyPublished);
        (await db.ServicePeriods.CountAsync()).Should().Be(2, "no duplicate rotations are created");
    }

    [Fact]
    public async Task An_unknown_cohort_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("publish-missing");
        await SeedGridAsync(db, students: 1);

        var result = await Publisher(db).PublishCohortAsync(999, false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.CohortNotFound(999));
    }

    [Fact]
    public async Task A_cohort_with_no_grid_cells_cannot_be_published()
    {
        await using var db = TestHarness.NewContext("publish-unconfigured");
        await SeedGridAsync(db, students: 2, withSlotAssignment: false);

        var result = await Publisher(db).PublishCohortAsync(CohortId, false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.ScheduleNotConfigured);
    }

    [Fact]
    public async Task A_cohort_with_no_students_cannot_be_published()
    {
        await using var db = TestHarness.NewContext("publish-empty");
        await SeedGridAsync(db, students: 0);

        var result = await Publisher(db).PublishCohortAsync(CohortId, false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.NoPlannedAssignments);
    }

    [Fact]
    public async Task Over_booking_a_service_beyond_its_capacity_is_refused()
    {
        await using var db = TestHarness.NewContext("publish-capacity");
        await SeedGridAsync(db, students: 25, capacity: 20);

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.CapacityExceeded");
        result.Error.Description.Should().Contain("25").And.Contain("20");
        (await db.ServicePeriods.CountAsync()).Should().Be(0, "nothing is materialised when the guard trips");
    }

    [Fact]
    public async Task Capacity_can_be_overridden_deliberately()
    {
        await using var db = TestHarness.NewContext("publish-force");
        await SeedGridAsync(db, students: 25, capacity: 20);

        var result = await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: true, default);

        result.IsSuccess.Should().BeTrue();
        (await db.ServicePeriods.CountAsync()).Should().Be(25);
    }

    [Fact]
    public async Task Exactly_filling_a_service_to_capacity_is_allowed()
    {
        await using var db = TestHarness.NewContext("publish-exact");
        await SeedGridAsync(db, students: 20, capacity: 20);

        var result = await Publisher(db).PublishCohortAsync(CohortId, false, default);

        result.IsSuccess.Should().BeTrue("capacity is a ceiling, not an exclusive bound");
    }

    [Fact]
    public async Task Occupancy_is_counted_across_stages_sharing_the_same_service()
    {
        await using var db = TestHarness.NewContext("publish-cross-stage");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 20;

        // Two cohorts of 12 routed through the SAME service over overlapping windows: 24 > 20.
        var mine = db.SeedCohort(stage, CohortId, "Groupe 10");
        var other = db.SeedCohort(stage, 11, "Groupe 11");
        var slot = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        db.SeedSlotAssignment(1, mine, slot, service);
        db.SeedSlotAssignment(2, other, slot, service);
        foreach (var cohort in new[] { mine, other })
            for (int i = 0; i < 12; i++)
                db.SeedAssignment(db.SeedRegistration($"E{cohort.Id}-{i}", "Test", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, false, default);

        result.IsFailure.Should().BeTrue("the other group already occupies the same service that month");
        result.Error.Code.Should().Be("Schedule.CapacityExceeded");
    }

    [Fact]
    public async Task Non_overlapping_windows_do_not_compete_for_capacity()
    {
        await using var db = TestHarness.NewContext("publish-no-overlap");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 20;

        var mine = db.SeedCohort(stage, CohortId, "Groupe 10");
        var other = db.SeedCohort(stage, 11, "Groupe 11");
        db.SeedSlotAssignment(1, mine, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);
        db.SeedSlotAssignment(2, other, db.SeedSlot(stage, 200, 2, P2Start, P2End), service);   // next month
        foreach (var cohort in new[] { mine, other })
            for (int i = 0; i < 15; i++)
                db.SeedAssignment(db.SeedRegistration($"E{cohort.Id}-{i}", "Test", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishCohortAsync(CohortId, false, default);

        result.IsSuccess.Should().BeTrue("students present in different months never share a bed");
    }

    [Fact]
    public async Task Publishing_a_whole_stage_skips_cohorts_that_are_already_published()
    {
        await using var db = TestHarness.NewContext("publish-stage-skip");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var first = db.SeedCohort(stage, CohortId, "Groupe 10");
        var second = db.SeedCohort(stage, 11, "Groupe 11");
        var slot = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        db.SeedSlotAssignment(1, first, slot, service);
        db.SeedSlotAssignment(2, second, slot, service);
        foreach (var cohort in new[] { first, second })
            for (int i = 0; i < 2; i++)
                db.SeedAssignment(db.SeedRegistration($"E{cohort.Id}-{i}", "Test", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();
        await Publisher(db).PublishCohortAsync(CohortId, true, default);

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, allowOverCapacity: true, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PublishedCohorts.Should().Be(1);
        result.Value.SkippedCohorts.Should().Be(1, "the already-published cohort is left alone");
        result.Value.PeriodsCreated.Should().Be(2);
    }

    [Fact]
    public async Task Publishing_a_stage_with_no_cohorts_is_a_no_op_rather_than_an_error()
    {
        await using var db = TestHarness.NewContext("publish-stage-empty");
        db.SeedCatalog();
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, false, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new PublishResult(0, 0, 0));
    }

    [Fact]
    public async Task A_stage_publish_can_be_narrowed_to_a_window_of_periods()
    {
        await using var db = TestHarness.NewContext("publish-stage-window");
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);
        db.SeedSlotAssignment(2, cohort, db.SeedSlot(stage, 200, 2, P2Start, P2End), service);
        db.SeedAssignment(db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, periodNumbers: [1],
            allowOverCapacity: true, ct: default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsCreated.Should().Be(1, "only period 1 was asked for");
        (await db.ServicePeriods.SingleAsync()).StartDate.Should().Be(P1Start);
    }
}
