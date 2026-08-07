using FluentAssertions;
using PGSH.Application.Stages.InternshipAssignments.Fiche;
using PGSH.Application.Stages.InternshipAssignments.GetRecord;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The student's own view of a stage: every rotation with its mark and verdict, the attendance tally,
// and — once the whole stage passes — the fiche de validation. Both reads recompute marks through
// StageScoring, the same helper the domain uses, so a record can never disagree with the stage note.
public class StudentStageRecordTests
{
    private static readonly DateOnly FirstStart  = new(2026, 3, 1);
    private static readonly DateOnly FirstEnd    = new(2026, 3, 31);
    private static readonly DateOnly SecondStart = new(2026, 4, 1);
    private static readonly DateOnly SecondEnd   = new(2026, 4, 30);

    private sealed record Scenario(InternshipAssignment Assignment, ServicePeriod First, ServicePeriod Second);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        var cardio = db.SeedService(1, "Cardiologie");
        var reanim = db.SeedService(2, "Réanimation");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        var first  = db.SeedPeriod(assignment, cardio, FirstStart, FirstEnd);
        var second = db.SeedPeriod(assignment, reanim, SecondStart, SecondEnd);
        assignment.Start().IsSuccess.Should().BeTrue();

        await db.SaveChangesAsync();
        return new Scenario(assignment, first, second);
    }

    /// <summary>The administration officialises the marks — what the fiche gate waits for.</summary>
    private static void Ratify(InternshipAssignment assignment) =>
        assignment.Validate().IsSuccess.Should().BeTrue();

    private static void Evaluate(InternshipAssignment assignment, ServicePeriod period, decimal mark)
    {
        assignment.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
        assignment.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = mark,
        }).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task The_record_lists_every_rotation_in_date_order()
    {
        await using var db = TestHarness.NewContext("record-order");
        var s = await SeedAsync(db);

        var result = await new GetStudentStageRecordQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Periods.Should().HaveCount(2);
        result.Value.Periods.Select(p => p.StartDate).Should().BeInAscendingOrder();
        result.Value.Periods.First().ServiceName.Should().Be("Cardiologie");
    }

    [Fact]
    public async Task Each_rotation_carries_the_mark_and_verdict_the_domain_computed()
    {
        await using var db = TestHarness.NewContext("record-marks");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 14m);
        Evaluate(s.Assignment, s.Second, 8m);
        await db.SaveChangesAsync();

        var result = await new GetStudentStageRecordQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        var periods = result.Value.Periods;
        periods.First(p => p.ServiceName == "Cardiologie").Mark.Should().Be(14m);
        periods.First(p => p.ServiceName == "Cardiologie").Validated.Should().BeTrue();
        periods.First(p => p.ServiceName == "Réanimation").Mark.Should().Be(8m);
        periods.First(p => p.ServiceName == "Réanimation").Validated.Should().BeFalse();
    }

    [Fact]
    public async Task The_stage_note_is_the_mean_and_one_failed_rotation_fails_the_stage()
    {
        await using var db = TestHarness.NewContext("record-rollup");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 14m);
        Evaluate(s.Assignment, s.Second, 8m);
        await db.SaveChangesAsync();

        var result = await new GetStudentStageRecordQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        result.Value.FinalScore.Should().Be(11m);
        result.Value.Result.Should().Be(StageAssignmentResult.NonValidé, "one failed rotation fails the stage");
        result.Value.AllPeriodsEvaluated.Should().BeTrue();
    }

    [Fact]
    public async Task A_partly_graded_stage_is_flagged_as_not_fully_evaluated()
    {
        await using var db = TestHarness.NewContext("record-partial");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 15m);
        await db.SaveChangesAsync();

        var result = await new GetStudentStageRecordQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        result.Value.AllPeriodsEvaluated.Should().BeFalse();
        result.Value.Result.Should().Be(StageAssignmentResult.NonÉvalué);
        result.Value.Periods.First(p => p.ServiceName == "Réanimation").Mark.Should().BeNull();
    }

    [Fact]
    public async Task The_attendance_tally_is_broken_down_by_status()
    {
        await using var db = TestHarness.NewContext("record-attendance");
        var s = await SeedAsync(db);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { Id = Guid.NewGuid(), ServicePeriodId = s.First.Id, Date = FirstStart,             Status = AttendanceStatus.Present },
            new AttendanceRecord { Id = Guid.NewGuid(), ServicePeriodId = s.First.Id, Date = FirstStart.AddDays(1),  Status = AttendanceStatus.Present },
            new AttendanceRecord { Id = Guid.NewGuid(), ServicePeriodId = s.First.Id, Date = FirstStart.AddDays(2),  Status = AttendanceStatus.Absent },
            new AttendanceRecord { Id = Guid.NewGuid(), ServicePeriodId = s.First.Id, Date = FirstStart.AddDays(3),  Status = AttendanceStatus.JustifiedAbsent },
            new AttendanceRecord { Id = Guid.NewGuid(), ServicePeriodId = s.First.Id, Date = FirstStart.AddDays(4),  Status = AttendanceStatus.Late });
        await db.SaveChangesAsync();

        var result = await new GetStudentStageRecordQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetStudentStageRecordQuery(s.Assignment.Id), default);

        var period = result.Value.Periods.First(p => p.ServiceName == "Cardiologie");
        period.PresentCount.Should().Be(2);
        period.AbsentCount.Should().Be(1);
        period.JustifiedAbsentCount.Should().Be(1);
        period.LateCount.Should().Be(1);
        period.Attendance.Should().HaveCount(5);
    }

    [Fact]
    public async Task An_unknown_assignment_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("record-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await new GetStudentStageRecordQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetStudentStageRecordQuery(missing), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AssignmentNotFound(missing));
    }

    [Fact]
    public async Task The_fiche_is_issued_once_the_whole_stage_passes_and_is_ratified()
    {
        await using var db = TestHarness.NewContext("fiche-issued");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 14m);
        Evaluate(s.Assignment, s.Second, 16m);
        Ratify(s.Assignment);
        await db.SaveChangesAsync();

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.StudentFullName.Should().Be("Sara Bennani");
        result.Value.StageName.Should().Be("Cardiologie");
        result.Value.FinalMark.Should().Be(15m);
        result.Value.Periods.Should().HaveCount(2);
        result.Value.Periods.Select(p => p.StartDate).Should().BeInAscendingOrder();
    }

    // The fiche is an official document: passing marks are not enough on their own, the
    // administration has to have ratified them. Otherwise a student could print an attestation
    // the moment the chef saved a grade, before Scolarité ever looked at it.
    [Fact]
    public async Task The_fiche_is_refused_while_the_marks_await_ratification()
    {
        await using var db = TestHarness.NewContext("fiche-unratified");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 14m);
        Evaluate(s.Assignment, s.Second, 16m);
        await db.SaveChangesAsync();

        s.Assignment.Result.Should().Be(StageAssignmentResult.Validé, "the marks say the stage passed");
        s.Assignment.Status.Should().Be(InternshipStatus.Evaluated, "but nobody has ratified them");

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.FicheNotAvailable);
    }

    // Refusing to ratify keeps the fiche shut even though the marks themselves passed.
    [Fact]
    public async Task The_fiche_is_refused_when_the_ratification_was_declined()
    {
        await using var db = TestHarness.NewContext("fiche-rejected");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 14m);
        Evaluate(s.Assignment, s.Second, 16m);
        s.Assignment.Reject().IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.FicheNotAvailable);
    }

    [Fact]
    public async Task The_fiche_is_refused_while_one_rotation_still_fails()
    {
        await using var db = TestHarness.NewContext("fiche-failed");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 14m);
        Evaluate(s.Assignment, s.Second, 6m);
        await db.SaveChangesAsync();

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(s.Assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.FicheNotAvailable);
    }

    [Fact]
    public async Task The_fiche_names_the_service_and_hospital_of_each_rotation()
    {
        await using var db = TestHarness.NewContext("fiche-services");
        var s = await SeedAsync(db);
        Evaluate(s.Assignment, s.First, 12m);
        Evaluate(s.Assignment, s.Second, 18m);
        Ratify(s.Assignment);
        await db.SaveChangesAsync();

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Periods.Select(p => p.ServiceName).Should().Contain(["Cardiologie", "Réanimation"]);
        result.Value.Periods.Should().OnlyContain(p => p.HospitalName == "CHU Ibn Sina");
        result.Value.Periods.First(p => p.ServiceName == "Réanimation").Mark.Should().Be(18m);
    }

    [Fact]
    public async Task An_interrupted_rotation_never_appears_on_the_fiche()
    {
        await using var db = TestHarness.NewContext("fiche-interrupted");
        var s = await SeedAsync(db);
        s.Second.IsInterrupted = true;              // cut short by a mid-stage transfer
        Evaluate(s.Assignment, s.First, 15m);
        Ratify(s.Assignment);
        await db.SaveChangesAsync();

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(s.Assignment.Id), default);

        result.IsSuccess.Should().BeTrue("the remaining rotation passed, so the stage is validated");
        result.Value.Periods.Should().ContainSingle()
            .Which.ServiceName.Should().Be("Cardiologie");
    }
}
