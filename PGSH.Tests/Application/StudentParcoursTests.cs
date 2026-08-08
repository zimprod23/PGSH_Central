using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Students.GetParcours;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The parcours is what the student portal reads instead of "the assignments of my current
// registration". A registration is one academic year; a parcours is the whole course. Folding the
// registrations together is the only way last year's stages survive the creation of this year's.
public class StudentParcoursTests
{
    private const int CardiologieId = TestHarness.StageId;   // 1
    private const int PediatrieId   = 2;
    private const int ServiceId     = 1;

    private sealed record Scenario(Registration Previous, Registration Current, Cohort PreviousCohort, Cohort CurrentCohort);

    // Two years at the same level: 2024-2025 then the current 2025-2026.
    private static Scenario SeedTwoYears(ApplicationDbContext db)
    {
        var cardio = db.SeedCatalog();
        db.SeedStage(PediatrieId, "Pédiatrie", coefficient: 3);
        db.SeedAcademicYear(TestHarness.PreviousYearId, "2024-2025",
            new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));
        db.SeedService(ServiceId, "Cardiologie");

        var previousCohort = db.SeedCohort(cardio, 20, "Groupe 20", TestHarness.PreviousYearId);
        var currentCohort  = db.SeedCohort(cardio, 10, "Groupe 10");

        var previous = db.SeedRegistration("Omar", "Tazi", previousCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);
        var current = db.SeedRegistration("Omar", "Tazi", currentCohort.AcademicGroup);

        // Same human being across both years — SeedRegistration mints a student per call.
        current.StudentId = previous.StudentId;
        current.Student   = previous.Student;

        return new Scenario(previous, current, previousCohort, currentCohort);
    }

    private static GetStudentParcoursQueryHandler Handler(
        ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db, authorizer ?? db.AdminAuthorizer());

    private static ParcoursYear YearOf(StudentParcoursResponse parcours, string label) =>
        parcours.Years.Single(y => y.AcademicYearLabel == label);

    [Fact]
    public async Task Every_registration_is_returned_most_recent_first_not_only_the_current_one()
    {
        await using var db = TestHarness.NewContext("parcours-all-years");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 13m, from: new DateOnly(2024, 10, 1));
        db.SeedGradedAssignment(s.Current,  s.CurrentCohort,  service, mark: 15m, from: new DateOnly(2025, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Previous.StudentId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Years.Select(y => y.AcademicYearLabel)
            .Should().Equal("2025-2026", "2024-2025");
        result.Value.Years.Should().OnlyContain(y => y.Stages.Count == 1);
    }

    [Fact]
    public async Task Only_the_registration_of_the_current_academic_year_is_flagged_current()
    {
        await using var db = TestHarness.NewContext("parcours-current-flag");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        YearOf(result.Value, "2025-2026").IsCurrent.Should().BeTrue();
        YearOf(result.Value, "2024-2025").IsCurrent.Should().BeFalse();
    }

    // The defect that started this: the dashboard counted every assignment as "planifié", so a stage
    // stayed in the planned bucket after it had been served, closed and marked.
    [Fact]
    public async Task A_graded_stage_leaves_the_planned_bucket_and_lands_on_its_verdict()
    {
        await using var db = TestHarness.NewContext("parcours-planned-vs-done");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        var pediatrie = db.Stages.Local.First(x => x.Id == PediatrieId);
        var pediatrieCohort = db.SeedCohort(pediatrie, 11, "Groupe 10 — Pédiatrie");

        db.SeedGradedAssignment(s.Current, s.CurrentCohort, service, mark: 14m, from: new DateOnly(2025, 10, 1));
        db.SeedAssignment(s.Current, pediatrieCohort);   // never started
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        var totals = result.Value.Totals;
        totals.Validated.Should().Be(1);
        totals.Planned.Should().Be(1, "only the untouched assignment is still merely planned");
        totals.Ongoing.Should().Be(0);
        totals.AwaitingVerdict.Should().Be(0);
        totals.Failed.Should().Be(0);
        totals.Total.Should().Be(2);
    }

    [Fact]
    public async Task A_failed_stage_counts_as_failed_not_as_completed()
    {
        await using var db = TestHarness.NewContext("parcours-failed");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        db.SeedGradedAssignment(s.Current, s.CurrentCohort, service, mark: 7m, from: new DateOnly(2025, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        result.Value.Totals.Failed.Should().Be(1);
        result.Value.Totals.Validated.Should().Be(0);
        YearOf(result.Value, "2025-2026").Stages.Single().Result
            .Should().Be(StageAssignmentResult.NonValidé);
    }

    // Rotations over, marks not all in: neither planned nor decided. Letting it fall back into
    // "planned" is what made a finished stage keep showing as upcoming.
    [Fact]
    public async Task A_stage_whose_rotations_are_over_but_unmarked_is_awaiting_its_verdict()
    {
        await using var db = TestHarness.NewContext("parcours-awaiting");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        var assignment = db.SeedAssignment(s.Current, s.CurrentCohort);
        var period = db.SeedPeriod(assignment, service,
            new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), started: false);
        assignment.Start();
        assignment.CompletePeriod(period.Id);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        var totals = result.Value.Totals;
        totals.AwaitingVerdict.Should().Be(1);
        totals.Planned.Should().Be(0);
        totals.Ongoing.Should().Be(0);

        var stage = YearOf(result.Value, "2025-2026").Stages.Single();
        stage.Status.Should().Be(InternshipStatus.Completed);
        stage.AllPeriodsEvaluated.Should().BeFalse();
        stage.PeriodsComplete.Should().Be(1);
        stage.PeriodsTotal.Should().Be(1);
    }

    [Fact]
    public async Task A_started_stage_counts_as_ongoing()
    {
        await using var db = TestHarness.NewContext("parcours-ongoing");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        var assignment = db.SeedAssignment(s.Current, s.CurrentCohort);
        db.SeedPeriod(assignment, service, new DateOnly(2025, 10, 1), new DateOnly(2025, 11, 30), started: false);
        assignment.Start();
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        result.Value.Totals.Ongoing.Should().Be(1);
        result.Value.Totals.Planned.Should().Be(0);
    }

    [Fact]
    public async Task Retakes_are_numbered_by_academic_year_across_registrations()
    {
        await using var db = TestHarness.NewContext("parcours-attempts");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 6m,  from: new DateOnly(2024, 10, 1));
        db.SeedGradedAssignment(s.Current,  s.CurrentCohort,  service, mark: 12m, from: new DateOnly(2025, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        YearOf(result.Value, "2024-2025").Stages.Single().AttemptNumber.Should().Be(1);
        YearOf(result.Value, "2025-2026").Stages.Single().AttemptNumber.Should().Be(2);
    }

    // A retake of an earlier level's stage hangs off the registration held now, so the year it appears
    // under is a 6th-year one while the stage itself is 1st-year work.
    [Fact]
    public async Task A_stage_carries_its_own_level_not_the_registrations()
    {
        await using var db = TestHarness.NewContext("parcours-cross-level");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        var sixthLevel = new Level { Id = 9, Label = "6ème année", Year = 6 };
        db.Levels.Add(sixthLevel);
        var sixthGroup = new AcademicGroup
        {
            Id = 60, Label = "Groupe 60", GroupNumber = 60, AcademicYearId = TestHarness.CurrentYearId,
        };
        db.AcademicGroups.Add(sixthGroup);
        var sixthReg = db.SeedRegistration("Omar", "Tazi", sixthGroup, levelId: 9);
        sixthReg.StudentId = s.Previous.StudentId;
        sixthReg.Student   = s.Previous.Student;

        // The retake still points at the level-1 stage through its cohort.
        db.SeedGradedAssignment(sixthReg, s.CurrentCohort, service, mark: 14m, from: new DateOnly(2026, 2, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Previous.StudentId), default);

        var sixthYear = result.Value.Years.Single(y => y.RegistrationId == sixthReg.Id);
        sixthYear.LevelId.Should().Be(9);
        sixthYear.Stages.Single().StageLevelId.Should().Be(TestHarness.LevelId);
        sixthYear.Stages.Single().StageLevelLabel.Should().Be("3ème année");
    }

    [Fact]
    public async Task Per_year_totals_are_scoped_to_that_year()
    {
        await using var db = TestHarness.NewContext("parcours-year-totals");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();

        db.SeedGradedAssignment(s.Previous, s.PreviousCohort, service, mark: 5m,  from: new DateOnly(2024, 10, 1));
        db.SeedGradedAssignment(s.Current,  s.CurrentCohort,  service, mark: 16m, from: new DateOnly(2025, 10, 1));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        YearOf(result.Value, "2024-2025").Totals.Failed.Should().Be(1);
        YearOf(result.Value, "2024-2025").Totals.Validated.Should().Be(0);
        YearOf(result.Value, "2025-2026").Totals.Validated.Should().Be(1);
        YearOf(result.Value, "2025-2026").Totals.Failed.Should().Be(0);
    }

    [Fact]
    public async Task A_registration_with_no_assignment_is_still_listed_as_an_empty_year()
    {
        await using var db = TestHarness.NewContext("parcours-empty-year");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        result.Value.Years.Should().HaveCount(2);
        result.Value.Years.Should().OnlyContain(y => y.Stages.Count == 0);
        result.Value.Totals.Total.Should().Be(0);
    }

    [Fact]
    public async Task The_rotation_span_covers_every_period_of_the_stage()
    {
        await using var db = TestHarness.NewContext("parcours-span");
        var s = SeedTwoYears(db);
        var service = db.Services.Local.First();
        var second = db.SeedService(2, "Pneumologie");

        var assignment = db.SeedAssignment(s.Current, s.CurrentCohort);
        db.SeedPeriod(assignment, service, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), complete: true);
        db.SeedPeriod(assignment, second,  new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 30));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        var stage = YearOf(result.Value, "2025-2026").Stages.Single();
        stage.StartDate.Should().Be(new DateOnly(2025, 10, 1));
        stage.EndDate.Should().Be(new DateOnly(2025, 11, 30));
        stage.PeriodsTotal.Should().Be(2);
        stage.PeriodsComplete.Should().Be(1);
    }

    [Fact]
    public async Task A_stage_with_no_rotation_planned_yet_has_no_span()
    {
        await using var db = TestHarness.NewContext("parcours-no-span");
        var s = SeedTwoYears(db);
        db.SeedAssignment(s.Current, s.CurrentCohort);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        var stage = YearOf(result.Value, "2025-2026").Stages.Single();
        stage.StartDate.Should().BeNull();
        stage.EndDate.Should().BeNull();
        stage.PeriodsTotal.Should().Be(0);
        stage.AllPeriodsEvaluated.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_student_is_reported_as_not_found()
    {
        await using var db = TestHarness.NewContext("parcours-missing-student");
        SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetStudentParcoursQuery(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Students.NotFound");
    }

    [Fact]
    public async Task A_caller_who_is_neither_the_administration_nor_the_student_is_refused()
    {
        await using var db = TestHarness.NewContext("parcours-stranger");
        var s = SeedTwoYears(db);
        await db.SaveChangesAsync();

        var result = await Handler(db, db.StrangerAuthorizer())
            .Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.DossierReadNotAllowed);
    }

    [Fact]
    public async Task A_student_may_read_their_own_parcours()
    {
        await using var db = TestHarness.NewContext("parcours-self");
        var s = SeedTwoYears(db);

        var keycloakId = Guid.NewGuid();
        s.Previous.Student.LinkIdentity(keycloakId.ToString());
        await db.SaveChangesAsync();

        var selfAuthorizer = new ExecutionAuthorizer(db, TestHarness.UserContext(keycloakId, Roles.Student));

        var result = await Handler(db, selfAuthorizer)
            .Handle(new GetStudentParcoursQuery(s.Current.StudentId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.StudentId.Should().Be(s.Current.StudentId);
    }
}
