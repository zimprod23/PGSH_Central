using FluentAssertions;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Evaluations;
using PGSH.Application.Stages.ServicePeriods;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// What a chef sees in "Mes services". Two rules, and the second was learned the hard way:
//
//  1. The scope is derived server-side from his identity — he can never read another chef's worklist.
//  2. The list is BOUNDED, by the period's own lifecycle. It used to return every period of every
//     service he leads, unpaginated, on the grounds that the client groups them and a page boundary
//     would cut a group in half. Measured on the live base 2026-08-29, that was 3 220 rows for one
//     chef's two services, reaching back to 2019, all mounted at once — which is what took his
//     browser down. ServicePeriodState splits them into four slices that partition the whole,
//     and only the slice asked for is fetched.
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

    private static async Task<ChefWorklistResponse> WorklistOf(
        ApplicationDbContext db, GetMyServicePeriodsQuery? query = null)
    {
        var result = await Handler(db).Handle(query ?? new GetMyServicePeriodsQuery(), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static async Task<List<ServicePeriodResponse>> WorklistAsync(
        ApplicationDbContext db, GetMyServicePeriodsQuery? query = null) =>
        (await WorklistOf(db, query)).Page.Items.ToList();

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
    public async Task Rotations_that_have_not_begun_stay_out_of_the_live_slice()
    {
        await using var db = TestHarness.NewContext("worklist-future");
        await SeedAsync(db);

        var items = await WorklistAsync(db);

        items.Should().NotContain(p => p.StudentFullName.Contains("Tazi"),
            "a future rotation is not yet work the chef can do");
        items.Should().HaveCount(3);
    }

    // The report that started this: a promotion published into two services, and its chef shown
    // nothing at all. Publishing leaves every period IsStarted = false and the worklist only ever
    // returned started rows, so "the schedule is published" and "there is no schedule" looked
    // identical from the one screen that has to tell them apart.
    [Fact]
    public async Task A_published_but_unstarted_rotation_is_visible_under_the_upcoming_slice()
    {
        await using var db = TestHarness.NewContext("worklist-upcoming");
        await SeedAsync(db);

        var upcoming = await WorklistAsync(db,
            new GetMyServicePeriodsQuery(State: ServicePeriodState.Planned));

        upcoming.Should().ContainSingle().Which.StudentFullName.Should().Contain("Tazi");
    }

    // Visible is not actionable, and the row says so itself: the client is told the state rather
    // than left to infer it from three booleans, so it cannot offer a button on a rotation the
    // administration has not opened.
    [Fact]
    public async Task An_upcoming_row_carries_the_planned_state()
    {
        await using var db = TestHarness.NewContext("worklist-upcoming-flag");
        await SeedAsync(db);

        var upcoming = await WorklistAsync(db,
            new GetMyServicePeriodsQuery(State: ServicePeriodState.Planned));
        var live = await WorklistAsync(db);

        upcoming.Should().OnlyContain(p => p.State == ServicePeriodState.Planned);
        live.Should().OnlyContain(p => p.State == ServicePeriodState.Underway);
    }

    // A chef who lands on an empty slice must still be told where his work is, or a bounded list
    // reintroduces the very confusion it was built to remove.
    [Fact]
    public async Task Every_slice_carries_the_size_of_all_four()
    {
        await using var db = TestHarness.NewContext("worklist-counts");
        await SeedAsync(db);

        var response = await WorklistOf(db,
            new GetMyServicePeriodsQuery(State: ServicePeriodState.Settled));

        response.Page.Items.Should().BeEmpty();
        response.Counts.Planned.Should().Be(1, "Tazi is published and not started");
        response.Counts.Underway.Should().Be(3);
        response.Counts.AwaitingEvaluation.Should().Be(0);
        response.Counts.Settled.Should().Be(0);
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
        result.Value.Page.Items.Should().BeEmpty();
        result.Value.Page.TotalCount.Should().Be(0);
        result.Value.Counts.Total.Should().Be(0);
    }

    // The four slices are a partition, not four filters that happen to be useful: every period of
    // the service falls in exactly one, so nothing can hide between them and the counts add up.
    [Fact]
    public async Task The_four_slices_partition_every_period_of_the_service()
    {
        await using var db = TestHarness.NewContext("worklist-partition");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        var planned = db.SeedRegistration("Omar", "Tazi", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(planned, cohort), service, Start, End, started: false);

        var openReg = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(openReg, cohort), service, Start, End, complete: false);

        var closedReg = db.SeedRegistration("Ali", "Amrani", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(closedReg, cohort), service, Start, End, complete: true);

        var markedReg = db.SeedRegistration("Nadia", "Fassi", cohort.AcademicGroup);
        db.SeedGradedAssignment(markedReg, cohort, service, 14m, Start);
        await db.SaveChangesAsync();

        var upcoming   = await WorklistAsync(db, new GetMyServicePeriodsQuery(State: ServicePeriodState.Planned));
        var current    = await WorklistAsync(db, new GetMyServicePeriodsQuery(State: ServicePeriodState.Underway));
        var toEvaluate = await WorklistAsync(db, new GetMyServicePeriodsQuery(State: ServicePeriodState.AwaitingEvaluation));
        var history    = await WorklistAsync(db, new GetMyServicePeriodsQuery(State: ServicePeriodState.Settled));

        upcoming.Should().ContainSingle().Which.StudentFullName.Should().Contain("Tazi");
        current.Should().ContainSingle().Which.StudentFullName.Should().Contain("Bennani");
        toEvaluate.Should().ContainSingle().Which.StudentFullName.Should().Contain("Amrani");
        history.Should().ContainSingle().Which.StudentFullName.Should().Contain("Fassi");

        var counts = (await WorklistOf(db)).Counts;
        counts.Total.Should().Be(4, "no period may fall between two slices, nor into both");
    }

    // Omitting the slice must never land on the archive — that is the payload nobody wants and the
    // one that grows without bound.
    [Fact]
    public async Task Asking_for_no_slice_gives_the_live_one_never_the_archive()
    {
        await using var db = TestHarness.NewContext("worklist-default-slice");
        await SeedAsync(db);

        var response = await WorklistOf(db);

        response.State.Should().Be(ServicePeriodState.Underway);
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

        // An evaluated rotation is settled, so it lives in the archive — never in the live slice.
        var row = (await WorklistAsync(db, new GetMyServicePeriodsQuery(State: ServicePeriodState.Settled)))
            .Should().ContainSingle().Subject;

        row.HasEvaluation.Should().BeTrue();
        row.IsComplete.Should().BeTrue();
        (await WorklistAsync(db)).Should().BeEmpty("nothing is left to do on it");
    }

    // ⚠ The search has to reach the store now that the list is a page: filtering the rows the client
    // happens to hold answers "no such student in this service" for anyone on another page, and the
    // chef cannot tell that apart from a real absence.
    [Fact]
    public async Task The_search_reaches_students_beyond_the_first_page()
    {
        await using var db = TestHarness.NewContext("worklist-search-paged");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        for (int i = 0; i < 40; i++)
        {
            var filler = db.SeedRegistration($"Etudiant{i:D3}", "Test", cohort.AcademicGroup);
            db.SeedPeriod(db.SeedAssignment(filler, cohort), service, Start, End);
        }
        // A later window, so she is deterministically last in the ascending order and cannot land on
        // the first page by an accident of key generation.
        var wanted = db.SeedRegistration("Zoubida", "Ouazzani", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(wanted, cohort), service,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        await db.SaveChangesAsync();

        var firstPage = await WorklistAsync(db, new GetMyServicePeriodsQuery(PageSize: 10));
        var found = await WorklistAsync(db,
            new GetMyServicePeriodsQuery(SearchTerm: "ouazzani", PageSize: 10));

        firstPage.Should().NotContain(p => p.StudentFullName.Contains("Ouazzani"),
            "the setup is only meaningful while she is off the first page");
        found.Should().ContainSingle().Which.StudentFullName.Should().Contain("Ouazzani");
    }

    // Every field of the predicate is lower-cased on both sides, or the search works for some
    // students and not others depending on how their record was typed.
    [Theory]
    [InlineData("BENNANI")]
    [InlineData("sara")]
    [InlineData("cne-sara")]
    public async Task The_search_is_case_insensitive_on_every_field(string term)
    {
        await using var db = TestHarness.NewContext($"worklist-search-{term}");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        registration.Student.CNE = "CNE-SARA";
        db.SeedPeriod(db.SeedAssignment(registration, cohort), service, Start, End);
        await db.SaveChangesAsync();

        var items = await WorklistAsync(db, new GetMyServicePeriodsQuery(SearchTerm: term));

        items.Should().ContainSingle();
    }

    // The badges answer "where is this student?" while a search is running, so they must narrow with
    // it. A count of the unsearched slice beside two searched rows is a number about a different
    // question, and the chef has no way to know which one he is reading.
    [Fact]
    public async Task The_search_narrows_the_slice_counts_too()
    {
        await using var db = TestHarness.NewContext("worklist-search-counts");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        var wanted = db.SeedRegistration("Sara", "Bennani", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(wanted, cohort), service, Start, End, started: false);

        var other = db.SeedRegistration("Ali", "Amrani", cohort.AcademicGroup);
        db.SeedPeriod(db.SeedAssignment(other, cohort), service, Start, End, started: false);
        await db.SaveChangesAsync();

        var counts = (await WorklistOf(db, new GetMyServicePeriodsQuery(SearchTerm: "bennani"))).Counts;

        counts.Planned.Should().Be(1);
        counts.Total.Should().Be(1);
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

    // This case used to assert the opposite — that every matching row comes back whatever the page
    // size — because the client groups window → group → student and a boundary cuts a group in half.
    // It does, and that is the price: not paging meant one chef card mounted 3 220 rows and took the
    // browser down with it. The slice keeps a page large enough that it is usually the whole slice.
    [Fact]
    public async Task A_slice_larger_than_one_page_is_paginated_rather_than_returned_whole()
    {
        await using var db = TestHarness.NewContext("worklist-paged");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        for (int i = 0; i < 130; i++)
        {
            var registration = db.SeedRegistration($"Etudiant{i:D3}", "Test", cohort.AcademicGroup);
            db.SeedPeriod(db.SeedAssignment(registration, cohort), service, Start, End);
        }
        await db.SaveChangesAsync();

        var page = (await WorklistOf(db, new GetMyServicePeriodsQuery(PageSize: 10))).Page;

        page.Items.Should().HaveCount(10);
        page.TotalCount.Should().Be(130, "the caller is told how many rows it can still reach");
        page.HasNextPage.Should().BeTrue();
    }

    // Every student of a window shares one start date, so ordering by date alone leaves the page
    // boundary to the store: rows silently drop out of one page and repeat on the next.
    [Fact]
    public async Task Paging_a_window_whose_rows_share_one_date_loses_and_repeats_nobody()
    {
        await using var db = TestHarness.NewContext("worklist-page-stability");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        for (int i = 0; i < 50; i++)
        {
            var registration = db.SeedRegistration($"Etudiant{i:D3}", "Test", cohort.AcademicGroup);
            db.SeedPeriod(db.SeedAssignment(registration, cohort), service, Start, End);
        }
        await db.SaveChangesAsync();

        var seen = new List<Guid>();
        for (int p = 1; p <= 5; p++)
            seen.AddRange((await WorklistAsync(db, new GetMyServicePeriodsQuery(PageNumber: p, PageSize: 10)))
                .Select(r => r.Id));

        seen.Should().HaveCount(50);
        seen.Distinct().Should().HaveCount(50, "a total order is what makes paging repeatable");
    }

    // The default page size is the ceiling, so an ordinary promotion in one service arrives whole
    // and the client grouping is not cut by a boundary nobody asked for.
    [Fact]
    public async Task A_slice_that_fits_the_default_page_arrives_whole()
    {
        await using var db = TestHarness.NewContext("worklist-one-page");
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(FirstServiceId, "Cardiologie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        for (int i = 0; i < 130; i++)
        {
            var registration = db.SeedRegistration($"Etudiant{i:D3}", "Test", cohort.AcademicGroup);
            db.SeedPeriod(db.SeedAssignment(registration, cohort), service, Start, End);
        }
        await db.SaveChangesAsync();

        var page = (await WorklistOf(db)).Page;

        page.Items.Should().HaveCount(130);
        page.HasNextPage.Should().BeFalse();
    }
}
