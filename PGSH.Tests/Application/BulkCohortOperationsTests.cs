using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Cohorts.Bulk;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The administration drives a whole group at once: start the rotation, close it, validate it. Each
// bulk action reports how many records it actually moved, is idempotent (re-running moves nothing),
// and can be narrowed to a window of periods so P1 closes while P2 keeps running.
public class BulkCohortOperationsTests
{
    private const int CohortId = 10;
    private const int Students = 3;

    private static readonly DateOnly P1Start = new(2026, 3, 1);
    private static readonly DateOnly P1End   = new(2026, 3, 31);
    private static readonly DateOnly P2Start = new(2026, 4, 1);
    private static readonly DateOnly P2End   = new(2026, 4, 30);

    /// <summary>A published cohort: every student has an inactive period on each of the two grid cells.</summary>
    private static async Task SeedPublishedAsync(ApplicationDbContext db, int periods = 2)
    {
        var stage = db.SeedCatalog();
        var first = db.SeedService(1, "Cardiologie");
        var second = db.SeedService(2, "Réanimation");
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");

        var slot1 = db.SeedSlot(stage, 100, 1, P1Start, P1End);
        var cell1 = db.SeedSlotAssignment(1, cohort, slot1, first);
        CohortSlotAssignment? cell2 = null;
        if (periods > 1)
            cell2 = db.SeedSlotAssignment(2, cohort, db.SeedSlot(stage, 200, 2, P2Start, P2End), second);

        for (int i = 0; i < Students; i++)
        {
            var assignment = db.SeedAssignment(
                db.SeedRegistration($"Etudiant{i}", "Test", cohort.AcademicGroup), cohort);
            Attach(db.SeedPeriod(assignment, first, P1Start, P1End, started: false), cell1);
            if (cell2 is not null)
                Attach(db.SeedPeriod(assignment, second, P2Start, P2End, started: false), cell2);
        }

        await db.SaveChangesAsync();

        static void Attach(ServicePeriod period, CohortSlotAssignment cell)
        {
            period.CohortSlotAssignmentId = cell.Id;
            period.CohortSlotAssignment = cell;
        }
    }

    private static async Task<List<InternshipAssignment>> LoadAsync(ApplicationDbContext db) =>
        await db.InternshipAssignments.Include(a => a.ServicePeriods)
            .Where(a => a.CurrentCohortId == CohortId).ToListAsync();

    [Fact]
    public async Task Starting_a_cohort_activates_every_rotation_and_reports_the_count()
    {
        await using var db = TestHarness.NewContext("bulk-start");
        await SeedPublishedAsync(db);

        var result = await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Students * 2);
        (await LoadAsync(db)).Should().OnlyContain(a => a.Status == InternshipStatus.Ongoing);
    }

    [Fact]
    public async Task Starting_can_be_narrowed_to_one_period_of_the_grid()
    {
        await using var db = TestHarness.NewContext("bulk-start-scoped");
        await SeedPublishedAsync(db);

        var result = await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId, PeriodNumbers: [1]), default);

        result.Value.Should().Be(Students, "only period 1 was asked for");
        var periods = (await LoadAsync(db)).SelectMany(a => a.ServicePeriods).ToList();
        periods.Where(p => p.StartDate == P1Start).Should().OnlyContain(p => p.IsStarted);
        periods.Where(p => p.StartDate == P2Start).Should().OnlyContain(p => !p.IsStarted);
    }

    [Fact]
    public async Task Starting_an_already_running_cohort_moves_nothing()
    {
        await using var db = TestHarness.NewContext("bulk-start-idempotent");
        await SeedPublishedAsync(db);
        await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);

        var result = await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task Starting_an_unpublished_cohort_is_refused()
    {
        await using var db = TestHarness.NewContext("bulk-start-unpublished");
        var stage = db.SeedCatalog();
        var cohort = db.SeedCohort(stage, CohortId, "Groupe 10");
        db.SeedAssignment(db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup), cohort);
        await db.SaveChangesAsync();

        var result = await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.ScheduleNotPublished);
    }

    [Fact]
    public async Task Closing_a_cohort_completes_every_running_rotation()
    {
        await using var db = TestHarness.NewContext("bulk-close");
        await SeedPublishedAsync(db);
        await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);

        var result = await new CompletePeriodsCommandHandler(db)
            .Handle(new CompletePeriodsCommand(CohortId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Students * 2);
        (await LoadAsync(db)).Should().OnlyContain(a => a.Status == InternshipStatus.Completed);
    }

    [Fact]
    public async Task Closing_can_be_narrowed_so_one_period_ends_while_the_next_runs_on()
    {
        await using var db = TestHarness.NewContext("bulk-close-scoped");
        await SeedPublishedAsync(db);
        await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);

        var result = await new CompletePeriodsCommandHandler(db)
            .Handle(new CompletePeriodsCommand(CohortId, PeriodNumbers: [1]), default);

        result.Value.Should().Be(Students);
        (await LoadAsync(db)).Should().OnlyContain(a => a.Status == InternshipStatus.Ongoing,
            "the stage is not over while period 2 is still open");
    }

    [Fact]
    public async Task Closing_a_cohort_that_never_started_moves_nothing()
    {
        await using var db = TestHarness.NewContext("bulk-close-planned");
        await SeedPublishedAsync(db);

        var result = await new CompletePeriodsCommandHandler(db)
            .Handle(new CompletePeriodsCommand(CohortId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0, "only Ongoing assignments are closed");
    }

    [Fact]
    public async Task Validating_a_cohort_only_touches_fully_evaluated_stages()
    {
        await using var db = TestHarness.NewContext("bulk-validate-partial");
        await SeedPublishedAsync(db, periods: 1);
        await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);
        await new CompletePeriodsCommandHandler(db).Handle(new CompletePeriodsCommand(CohortId), default);

        // Grade only one of the three students.
        var assignments = await LoadAsync(db);
        var graded = assignments.First();
        graded.SubmitEvaluation(graded.ServicePeriods.Single().Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = 15m,
        }).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var result = await new ValidateCohortAssignmentsCommandHandler(db)
            .Handle(new ValidateCohortAssignmentsCommand(CohortId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1, "the two ungraded stages are not eligible");
        (await LoadAsync(db)).Count(a => a.Status == InternshipStatus.Validated).Should().Be(1);
    }

    [Fact]
    public async Task Validating_a_fully_evaluated_cohort_records_the_verdict_for_everyone()
    {
        await using var db = TestHarness.NewContext("bulk-validate-all");
        await SeedPublishedAsync(db, periods: 1);
        await new StartCohortAssignmentsCommandHandler(db)
            .Handle(new StartCohortAssignmentsCommand(CohortId), default);
        await new CompletePeriodsCommandHandler(db).Handle(new CompletePeriodsCommand(CohortId), default);

        foreach (var assignment in await LoadAsync(db))
            assignment.SubmitEvaluation(assignment.ServicePeriods.Single().Id, new ServiceEvaluation
            {
                Mode = EvaluationMode.Numeric, TotalScore = 13m,
            }).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var result = await new ValidateCohortAssignmentsCommandHandler(db)
            .Handle(new ValidateCohortAssignmentsCommand(CohortId), default);

        result.Value.Should().Be(Students);
        (await LoadAsync(db)).Should().OnlyContain(a => a.Result == StageAssignmentResult.Validé);
    }

    [Fact]
    public async Task Bulk_actions_on_an_unknown_cohort_move_nothing_rather_than_failing()
    {
        await using var db = TestHarness.NewContext("bulk-unknown");
        await SeedPublishedAsync(db);

        var validated = await new ValidateCohortAssignmentsCommandHandler(db)
            .Handle(new ValidateCohortAssignmentsCommand(999), default);
        var completed = await new CompletePeriodsCommandHandler(db)
            .Handle(new CompletePeriodsCommand(999), default);

        validated.IsSuccess.Should().BeTrue();
        validated.Value.Should().Be(0);
        completed.IsSuccess.Should().BeTrue();
        completed.Value.Should().Be(0);
    }
}
