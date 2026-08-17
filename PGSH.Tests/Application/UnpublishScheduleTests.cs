using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cohorts.UnpublishSchedule;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Undoing a publication, and the two things that must survive it.
///
/// <para>⚠ <b>Deleting a <c>ServicePeriod</c> is not bookkeeping.</b> <c>ServiceEvaluation</c>,
/// <c>AttendanceRecord</c>, <c>PeriodPause</c> and <c>Delocalization</c> all cascade from it, so an
/// unguarded unpublish destroys every mark a chef entered and every day of attendance recorded — and
/// used to do exactly that, silently, on a cohort mid-rotation.</para>
///
/// <para>⚠ <b>Ad-hoc periods are not part of a publication and are never touched.</b> A period with no
/// cell behind it is imported history, a délocalisation or a revalidation. None came from a
/// répartition and none can be recreated by publishing one.</para>
/// </summary>
public class UnpublishScheduleTests
{
    private const int ServiceId = 1;
    private const int CohortId  = 10;

    private static readonly DateOnly P1Start = new(2026, 3, 1);
    private static readonly DateOnly P1End   = new(2026, 3, 31);

    private static SchedulePublisher Publisher(ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new ServiceIntakeCalculator(db));

    private static UnpublishCohortScheduleCommandHandler Handler(ApplicationDbContext db) => new(db);

    private static async Task<(Cohort Cohort, Service Service)> SeedPublishedAsync(
        ApplicationDbContext db, int students = 2)
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 200;
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);

        for (int i = 0; i < students; i++)
            db.SeedAssignment(db.SeedRegistration($"E{i}", "Test", cohort.AcademicGroup), cohort);

        await db.SaveChangesAsync();
        await Publisher(db).PublishCohortAsync(CohortId, allowOverCapacity: false, default);
        return (cohort, service);
    }

    [Fact]
    public async Task Unpublishing_a_planned_rotation_removes_its_periods_and_coverage()
    {
        await using var db = TestHarness.NewContext(nameof(Unpublishing_a_planned_rotation_removes_its_periods_and_coverage));
        await SeedPublishedAsync(db);

        var result = await Handler(db).Handle(new UnpublishCohortScheduleCommand(CohortId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsRemoved.Should().Be(2);
        (await db.ServicePeriods.CountAsync()).Should().Be(0);
        (await db.ServicePeriodSlotCoverage.CountAsync()).Should().Be(0, "coverage cascades with the period");
    }

    [Fact]
    public async Task Unpublishing_returns_the_assignments_to_Planned()
    {
        await using var db = TestHarness.NewContext(nameof(Unpublishing_returns_the_assignments_to_Planned));
        await SeedPublishedAsync(db);

        foreach (var assignment in await db.InternshipAssignments.Include(a => a.ServicePeriods).ToListAsync())
            assignment.Start();
        await db.SaveChangesAsync();

        await Handler(db).Handle(new UnpublishCohortScheduleCommand(CohortId, Force: true), default);

        var after = await db.InternshipAssignments.ToListAsync();
        after.Should().OnlyContain(a => a.Status == InternshipStatus.Planned,
            "an assignment with no periods left is not underway");
        after.Should().OnlyContain(a => a.FinalScore == null);
    }

    [Fact]
    public async Task Unpublishing_a_rotation_that_has_started_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Unpublishing_a_rotation_that_has_started_is_refused));
        await SeedPublishedAsync(db);

        foreach (var assignment in await db.InternshipAssignments.Include(a => a.ServicePeriods).ToListAsync())
            assignment.Start();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new UnpublishCohortScheduleCommand(CohortId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.Underway");
        (await db.ServicePeriods.CountAsync()).Should().Be(2, "a refusal deletes nothing");
    }

    [Fact]
    public async Task The_refusal_names_what_would_be_lost()
    {
        await using var db = TestHarness.NewContext(nameof(The_refusal_names_what_would_be_lost));
        var (_, service) = await SeedPublishedAsync(db, students: 1);

        var assignment = await db.InternshipAssignments.Include(a => a.ServicePeriods).SingleAsync();
        var period = assignment.ServicePeriods.Single();
        assignment.Start();
        assignment.CompletePeriod(period.Id);
        assignment.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = 15m,
        });
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), ServicePeriodId = period.Id,
            Date = P1Start, Status = AttendanceStatus.Present,
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new UnpublishCohortScheduleCommand(CohortId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("1 portent une évaluation");
        result.Error.Description.Should().Contain("1 journée(s) de présence");
    }

    [Fact]
    public async Task Forcing_it_through_proceeds_and_reports_the_damage()
    {
        await using var db = TestHarness.NewContext(nameof(Forcing_it_through_proceeds_and_reports_the_damage));
        await SeedPublishedAsync(db, students: 1);

        var assignment = await db.InternshipAssignments.Include(a => a.ServicePeriods).SingleAsync();
        var period = assignment.ServicePeriods.Single();
        assignment.Start();
        assignment.CompletePeriod(period.Id);
        assignment.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = 15m,
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new UnpublishCohortScheduleCommand(CohortId, Force: true), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsRemoved.Should().Be(1);
        result.Value.EvaluationsLost.Should().Be(1);
        (await db.ServiceEvaluation.CountAsync()).Should().Be(0, "the evaluation cascaded away with its period");

        var after = await db.InternshipAssignments.SingleAsync();
        after.Status.Should().Be(InternshipStatus.Planned);
        after.FinalScore.Should().BeNull("a note computed from marks that no longer exist is a lie");
    }

    [Fact]
    public async Task Ad_hoc_periods_survive_an_unpublish()
    {
        await using var db = TestHarness.NewContext(nameof(Ad_hoc_periods_survive_an_unpublish));
        var (cohort, service) = await SeedPublishedAsync(db, students: 1);

        // The shape the imported Access history has: a period nobody planned through a cell.
        var assignment = await db.InternshipAssignments.Include(a => a.ServicePeriods).SingleAsync();
        db.SeedPeriod(assignment, service, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), complete: true);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new UnpublishCohortScheduleCommand(CohortId, Force: true), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PeriodsRemoved.Should().Be(1);
        result.Value.AdHocPeriodsKept.Should().Be(1);

        var remaining = await db.ServicePeriods.ToListAsync();
        remaining.Should().ContainSingle().Which.CohortSlotAssignmentId.Should().BeNull();
    }

    [Fact]
    public async Task Unpublishing_a_cohort_that_was_never_published_is_reported()
    {
        await using var db = TestHarness.NewContext(nameof(Unpublishing_a_cohort_that_was_never_published_is_reported));
        var stage = db.SeedCatalog();
        db.SeedCohort(stage, CohortId, "Groupe 10");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new UnpublishCohortScheduleCommand(CohortId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schedule.NotPublished");
    }

    // ─── Publishing never lands on top of a stage already served ─────────────

    [Fact]
    public async Task Publishing_skips_a_student_who_already_has_a_period_for_the_stage()
    {
        // The live shape: the Access import gave every 2025-2026 assignment one ad-hoc period, and
        // publishing the new grid on top would give each student a second set for the same stage —
        // averaged into the note and waited on by the lifecycle.
        await using var db = TestHarness.NewContext(nameof(Publishing_skips_a_student_who_already_has_a_period_for_the_stage));
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 200;
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);

        var served = db.SeedAssignment(db.SeedRegistration("Deja", "Servi", cohort.AcademicGroup), cohort);
        db.SeedPeriod(served, service, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), complete: true);
        db.SeedAssignment(db.SeedRegistration("A", "Planifier", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        await Publisher(db).PublishCohortAsync(CohortId, false, default);

        (await db.ServicePeriods.CountAsync(p => p.InternshipAssignmentId == served.Id))
            .Should().Be(1, "the historical period is left exactly as it was, and no second one joins it");
        (await db.ServicePeriods.CountAsync(p => p.CohortSlotAssignmentId != null))
            .Should().Be(1, "only the student with nothing on record is scheduled");
    }

    [Fact]
    public async Task Publishing_a_stage_reports_how_many_assignments_it_left_alone()
    {
        await using var db = TestHarness.NewContext(nameof(Publishing_a_stage_reports_how_many_assignments_it_left_alone));
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        service.Capacity = 200;
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedSlotAssignment(1, cohort, db.SeedSlot(stage, 100, 1, P1Start, P1End), service);

        for (int i = 0; i < 3; i++)
        {
            var assignment = db.SeedAssignment(db.SeedRegistration($"S{i}", "Servi", cohort.AcademicGroup), cohort);
            db.SeedPeriod(assignment, service, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), complete: true);
        }
        db.SeedAssignment(db.SeedRegistration("Neuf", "Test", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        var result = await Publisher(db).PublishStageAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, false, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.SkippedAlreadyServed.Should().Be(3);
        result.Value.PeriodsCreated.Should().Be(1);
    }
}
