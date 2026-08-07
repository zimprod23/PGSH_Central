using FluentAssertions;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Evaluations;
using PGSH.Application.Stages.ServicePeriods;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// What a chef sees in "Mes services". The scope is derived server-side from his identity — he can
// never read another chef's worklist — and only rotations that have actually begun are his concern.
public class ChefWorklistScopeTests
{
    private const int FirstServiceId   = 1;
    private const int SecondServiceId  = 2;
    private const int ForeignServiceId = 3;

    private static readonly Guid ChefIdentity = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);

    /// <summary>A chef leading two services, plus a third service led by nobody he knows.</summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var first   = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var second  = db.SeedService(SecondServiceId, "Réanimation", chef);
        var foreign = db.SeedService(ForeignServiceId, "Pédiatrie");

        var cohortA = db.SeedCohort(stage, 10, "Groupe 10");
        var cohortB = db.SeedCohort(stage, 20, "Groupe 20");

        Add("Sara", "Bennani", cohortA, first,  started: true);
        Add("Ali", "Amrani",   cohortA, first,  started: true);
        Add("Nadia", "Fassi",  cohortB, second, started: true);
        Add("Omar", "Tazi",    cohortB, second, started: false);   // future rotation — hidden
        Add("Hind", "Berrada", cohortA, foreign, started: true);   // another chef's service

        await db.SaveChangesAsync();

        void Add(string first_, string last, Cohort cohort, Service service, bool started)
        {
            var registration = db.SeedRegistration(first_, last, cohort.AcademicGroup);
            var assignment = db.SeedAssignment(registration, cohort);
            db.SeedPeriod(assignment, service, Start, End, started);
        }
    }

    private static GetMyServicePeriodsQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(ChefIdentity)));

    private static async Task<List<ServicePeriodResponse>> WorklistAsync(
        ApplicationDbContext db, GetMyServicePeriodsQuery? query = null)
    {
        var result = await Handler(db).Handle(query ?? new GetMyServicePeriodsQuery(), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value.Items.ToList();
    }

    [Fact]
    public async Task Only_the_chefs_own_services_appear()
    {
        await using var db = TestHarness.NewContext("worklist-scope");
        await SeedAsync(db);

        var items = await WorklistAsync(db);

        items.Should().OnlyContain(p => p.ServiceId == FirstServiceId || p.ServiceId == SecondServiceId);
        items.Should().NotContain(p => p.StudentFullName.Contains("Berrada"),
            "a chef never sees another chef's service");
    }

    [Fact]
    public async Task Rotations_that_have_not_begun_stay_hidden()
    {
        await using var db = TestHarness.NewContext("worklist-future");
        await SeedAsync(db);

        var items = await WorklistAsync(db);

        items.Should().NotContain(p => p.StudentFullName.Contains("Tazi"),
            "a future rotation is not yet the chef's concern");
        items.Should().HaveCount(3);
    }

    [Fact]
    public async Task The_service_filter_narrows_to_that_service()
    {
        await using var db = TestHarness.NewContext("worklist-filter");
        await SeedAsync(db);

        var items = await WorklistAsync(db, new GetMyServicePeriodsQuery(ServiceId: FirstServiceId));

        items.Should().HaveCount(2);
        items.Should().OnlyContain(p => p.ServiceId == FirstServiceId);
    }

    [Fact]
    public async Task Asking_for_a_service_he_does_not_lead_yields_nothing()
    {
        await using var db = TestHarness.NewContext("worklist-foreign-filter");
        await SeedAsync(db);

        var items = await WorklistAsync(db, new GetMyServicePeriodsQuery(ServiceId: ForeignServiceId));

        items.Should().BeEmpty("the requested service is silently dropped, never honoured");
    }

    [Fact]
    public async Task An_employee_who_leads_no_service_gets_an_empty_worklist()
    {
        await using var db = TestHarness.NewContext("worklist-not-chef");
        await SeedAsync(db);
        var stranger = new GetMyServicePeriodsQueryHandler(
            db, new ExecutionAuthorizer(db, TestHarness.UserContext(Guid.NewGuid())));

        var result = await stranger.Handle(new GetMyServicePeriodsQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task The_completion_filter_separates_open_rotations_from_closed_ones()
    {
        await using var db = TestHarness.NewContext("worklist-complete");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        var openReg = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(openReg, cohort), service, Start, End, complete: false);
        var closedReg = db.SeedRegistration("Ali", "Amrani", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(closedReg, cohort), service, Start, End, complete: true);
        await db.SaveChangesAsync();

        var open   = await WorklistAsync(db, new GetMyServicePeriodsQuery(IsComplete: false));
        var closed = await WorklistAsync(db, new GetMyServicePeriodsQuery(IsComplete: true));

        open.Should().ContainSingle().Which.StudentFullName.Should().Contain("Bennani");
        closed.Should().ContainSingle().Which.StudentFullName.Should().Contain("Amrani");
    }

    [Fact]
    public async Task The_worklist_carries_the_group_stage_and_hospital_a_chef_groups_by()
    {
        await using var db = TestHarness.NewContext("worklist-labels");
        await SeedAsync(db);

        var row = (await WorklistAsync(db)).First(p => p.StudentFullName.Contains("Bennani"));

        row.AcademicGroupLabel.Should().Be("Groupe 10");
        row.StageName.Should().Be("Cardiologie");
        row.HospitalName.Should().Be("CHU Ibn Sina");
        row.LevelLabel.Should().Be("3ème année");
    }

    [Fact]
    public async Task A_suspended_rotation_surfaces_as_paused_with_its_motif()
    {
        await using var db = TestHarness.NewContext("worklist-paused");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        var period = db.SeedPeriod(assignment, service, Start, End);
        assignment.PausePeriod(period.Id, new DateOnly(2026, 3, 10), PauseKind.Exam, "Semaine d'examens")
            .IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var row = (await WorklistAsync(db)).Should().ContainSingle().Subject;

        row.IsPaused.Should().BeTrue();
        row.PauseReason.Should().Be("Semaine d'examens");
    }

    [Fact]
    public async Task An_evaluated_rotation_is_flagged_as_such()
    {
        await using var db = TestHarness.NewContext("worklist-evaluated");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        var period = db.SeedPeriod(assignment, service, Start, End);
        assignment.Start();
        assignment.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
        assignment.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = 14m,
        }).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var row = (await WorklistAsync(db)).Should().ContainSingle().Subject;

        row.HasEvaluation.Should().BeTrue();
        row.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task The_worklist_is_ordered_by_start_date_so_windows_group_cleanly()
    {
        await using var db = TestHarness.NewContext("worklist-order");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        var later = db.SeedRegistration("Later", "Student", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(later, cohort), service, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        var earlier = db.SeedRegistration("Earlier", "Student", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(earlier, cohort), service, Start, End);
        await db.SaveChangesAsync();

        var items = await WorklistAsync(db);

        items.Select(p => p.StartDate).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Every_matching_row_is_returned_so_client_side_grouping_loses_nobody()
    {
        await using var db = TestHarness.NewContext("worklist-unpaged");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        for (int i = 0; i < 130; i++)
        {
            var registration = db.SeedRegistration($"Etudiant{i}", "Test", cohort.AcademicGroup);
            db.SeedPeriod(db.SeedAssignment(registration, cohort), service, Start, End);
        }
        await db.SaveChangesAsync();

        var items = await WorklistAsync(db, new GetMyServicePeriodsQuery(PageSize: 10));

        items.Should().HaveCount(130, "a service can hold far more periods than one page");
    }
}
