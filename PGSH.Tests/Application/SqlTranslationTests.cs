using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.AcademicGroups.Placements;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Application.Hospitals.Coverage;
using PGSH.Application.Hospitals.Services.OccupancyReport;
using PGSH.Domain.Stages;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Progression;
using PGSH.Application.Stages.Cohorts.GetByStage;
using PGSH.Application.Stages.Schedule;
using PGSH.Application.Stages.Slots;
using PGSH.Application.Students.Export;
using PGSH.Application.Stages.Export;
using PGSH.Application.Students.Registrations.Holds;
using PGSH.Application.Students.Registrations.ReinscriptionSheet;
using PGSH.Application.Students.Registrations.Inscription;
using PGSH.Application.Students.Registrations.ReinscriptionSheet;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Cnpn.Effectivity;
using PGSH.Application.Stages.Cnpn.SeedFromHistory;
using PGSH.Application.Stages.Cnpn.GetCnpnVersions;
using PGSH.Application.Stages.Cnpn.Targeting;
using PGSH.Application.Stages.GetMany;
using PGSH.Application.Stages.Revalidation;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Does the query survive being turned into SQL?
///
/// <para>⚠ <b>The suite's oldest blind spot, and it bit for real on 2026-08-26.</b>
/// <c>UseInMemoryDatabase</c> executes LINQ against objects: it never translates anything, so a query
/// Npgsql cannot compile passes every handler test and every endpoint test, then throws on the first
/// request that reaches the real base. <c>CohortProvisioner</c> did exactly that — « Unable to
/// translate a collection subquery in a projection… » — and it took down the macro plan for the whole
/// 6ᵉ année with 1 004 tests green.</para>
///
/// <para><b>No database is needed to catch it.</b> Translation happens when the query is
/// <i>compiled</i>, before a connection is opened, so a context built on the Npgsql provider with a
/// connection string pointing nowhere is enough: <c>ToQueryString()</c> either returns SQL or throws
/// the translation error. That is not a substitute for Testcontainers — nothing here proves the SQL
/// <em>returns the right rows</em> — but it closes the half that costs a 500 in production.</para>
///
/// <para>Add a case here whenever a query gets a shape the provider might refuse: a collection
/// subquery inside a projection, <c>Distinct</c> or <c>GroupBy</c> over a computed element, a
/// client-side method in a predicate.</para>
///
/// <para><b>The macro-plan path is swept end to end</b> — <c>CohortProvisioner</c> →
/// <c>StudentAffectationService</c> → <c>RotationArranger</c> (with
/// <c>GroupScheduleConflictGuard</c> and <c>ServiceOccupancyCalculator</c>) →
/// <c>SchedulePublisher</c>. Only the provisioner had been proven to compile; the rest was covered
/// by handler tests alone, which is the coverage that was green while the plan was dying. ⚠ The
/// publisher's three queries had never run against PostgreSQL <em>at all</em>: the Med6 rehearsal of
/// 2026-08-26 ran with <c>publish: false</c> and the base holds 0 grid-linked périodes, so the first
/// real publication would have been the first execution.</para>
///
/// <para>⚠ <b>Compiling is not running.</b> Every case here says the query becomes SQL; not one says
/// the SQL returns the right rows, and none of them opens a connection. That half still needs
/// Testcontainers.</para>
///
/// <para>⚠ <b>…and a projection is not a predicate.</b> Measured while proving these cases bite: a
/// client-side method call in the final <c>Select</c> does <em>not</em> fail here, because EF
/// evaluates the top-level projection on the client by design — <c>ToQueryString()</c> happily
/// returns SQL for it. The same call in a <c>Where</c> throws. So what this file catches is a query
/// the provider <i>refuses</i>, not every query that reaches the database in a shape somebody would
/// want; a projection that quietly client-evaluates is a performance question, and it belongs to the
/// half that needs a real database.</para>
/// </summary>
public class SqlTranslationTests
{
    /// <summary>
    /// The Stages page's « which text states this figure » read. It exists precisely because
    /// expressing it the obvious way — a collection of each text's figures inside the row projection
    /// — is the shape that killed the macro plan, so the query it replaced must be provably SQL.
    /// </summary>
    /// <summary>
    /// The revalidation reads. <c>PriorAttemptsQuery</c> is the one that matters: it folds two
    /// aggregates over the <c>ServicePeriods</c> collection inside the row projection — an ordered
    /// <c>FirstOrDefault</c> and a <c>Max</c> — which is the family Npgsql refuses when the element
    /// carries no key. It ran unnamed inside the command for a year, so this pins what was already
    /// load-bearing rather than guarding something new.
    /// </summary>
    [Fact]
    public void The_revalidation_planner_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string priors = RevalidationPlanner
            .PriorAttemptsQuery(db, Guid.NewGuid(), stageId: 2, excludingRegistrationId: Guid.NewGuid())
            .ToQueryString();

        priors.Should().Contain("ServicePeriods");
        priors.Should().Contain("InternshipAssignments");

        RevalidationPlanner
            .ExistingAssignmentQuery(db, Guid.NewGuid(), stageId: 2)
            .ToQueryString().Should().Contain("InternshipAssignments");
    }

    /// <summary>
    /// The context read's three. <c>GoverningTextQuery</c> carries two correlated scalar sub-selects
    /// in its projection so that a text stating nothing comes back as null rather than dropping out
    /// of the result — the shape has to be proven translatable for that choice to hold.
    /// </summary>
    [Fact]
    public void The_revalidation_context_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        GetRevalidationContextQueryHandler
            .GoverningTextQuery(db, cnpnVersionId: 1, levelId: 1, stageId: 2)
            .ToQueryString().Should().Contain("CurriculumStages");

        GetRevalidationContextQueryHandler
            .FailureDetailQuery(db, Guid.NewGuid(), stageId: 2)
            .ToQueryString().Should().Contain("ServicePeriods");

        GetRevalidationContextQueryHandler
            .CohortOptionsQuery(db, stageId: 2, academicYearId: 7)
            .ToQueryString().Should().Contain("Cohorts");
    }

    [Fact]
    public void The_stage_text_figures_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = GetStagesQueryHandler.TextFiguresQuery(db, [1, 2, 3]).ToQueryString();

        sql.Should().Contain("CurriculumStages");
        sql.Should().Contain("CnpnVersions");
    }

    [Fact]
    public void The_cohort_provisioners_roster_text_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = CohortProvisioner.GroupTextsQuery(db, [1, 2, 3]).ToQueryString();

        sql.Should().Contain("DISTINCT");
        sql.Should().Contain("Registrations");
    }

    /// <summary>
    /// The shape it must never go back to, kept executable because a comment does not fail a build.
    ///
    /// <para>This is the query as it was written when the CNPN moved onto the registration: the
    /// collection subquery's element is <c>r.CnpnVersionId ?? r.Student.CnpnVersionId</c> — a computed
    /// value carrying no key — and <c>Distinct()</c> then leaves the provider unable to correlate the
    /// rows back to their roster.</para>
    ///
    /// <para>⚠ If EF Core ever learns to translate it, this test fails. That is not a regression:
    /// delete the case and note the version it started working in. It asserts a provider limitation,
    /// which is exactly why the production query is shaped the way it is.</para>
    /// </summary>
    [Fact]
    public void The_collection_subquery_that_broke_the_macro_plan_still_does_not_compile()
    {
        using var db = TestHarness.NewNpgsqlContext();

        var offending = db.AcademicGroups
            .AsNoTracking()
            .Select(g => new
            {
                g.Id,
                CnpnVersionIds = g.Registrations
                    .Where(r => r.CnpnVersionId != null || r.Student.CnpnVersionId != null)
                    .Select(r => r.CnpnVersionId ?? r.Student.CnpnVersionId!.Value)
                    .Distinct()
                    .ToList(),
            });

        var translating = () => offending.ToQueryString();

        translating.Should().Throw<InvalidOperationException>()
            .WithMessage("*collection subquery in a projection*");
    }

    [Fact]
    public void The_affectations_stage_cohort_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = StudentAffectationService
            .StageCohortsQuery(db, stageId: 40, academicYearId: 7, partitionLabels: null)
            .ToQueryString();

        sql.Should().Contain("Cohorts");
    }

    /// <summary>
    /// The partition-scoped form is a separate compilation: the labels reach the predicate as a
    /// <c>Contains</c> over a collection of <c>string</c>, which is not the same translation as the
    /// <c>int</c> lists everywhere else on this path.
    /// </summary>
    [Fact]
    public void The_affectations_stage_cohort_query_compiles_when_scoped_to_partitions()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = StudentAffectationService
            .StageCohortsQuery(db, stageId: 40, academicYearId: 7, partitionLabels: ["A", "B"])
            .ToQueryString();

        sql.Should().Contain("RotationGroup");
    }

    /// <summary>
    /// ⚠ The projection counts a navigation collection — <c>c.Assignments.Count</c> — which is the
    /// same family as the query that broke the macro plan. It resolves to a correlated
    /// <c>COUNT(*)</c> rather than a collection the provider must correlate row by row, and that
    /// difference is the provider's to make, not one that can be read off the C#.
    /// </summary>
    [Fact]
    public void The_arrangers_cohort_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = RotationArranger.CohortsQuery(db, stageId: 40, academicYearId: 7).ToQueryString();

        sql.Should().Contain("Cohorts");
        sql.Should().Contain("count(*)");
    }

    /// <summary>
    /// Four navigation hops, and a null-coalesce over a string concatenated with an <c>int</c>
    /// (<c>Level.Label ?? "niveau " + LevelId</c>) — a shape that has to become a cast and a
    /// concatenation in SQL or nothing at all.
    /// </summary>
    [Fact]
    public void The_group_conflict_guards_placement_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = GroupScheduleConflictGuard
            .PlacementsQuery(db, academicGroupIds: [1, 2], ignoredSlotIds: [3])
            .ToQueryString();

        sql.Should().Contain("StageSlots");
    }

    [Fact]
    public void The_occupancy_calculators_entry_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = ServiceOccupancyCalculator.EntriesQuery(db, serviceIds: [1, 2]).ToQueryString();

        sql.Should().Contain("CohortSlotAssignments");
        sql.Should().Contain("count(*)");
    }

    /// <summary>
    /// The per-cohort publish, which the macro plan does not use and a human does — one cohorte
    /// published from its own page. It shares nothing with <c>PublishStageAsync</c> but the class.
    /// </summary>
    [Fact]
    public void The_publishers_per_cohort_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        SchedulePublisher.PublishedAssignmentsQuery(db, cohortId: 1).ToQueryString()
            .Should().Contain("EXISTS");

        SchedulePublisher.UnservedAssignmentIdsQuery(db, cohortId: 1).ToQueryString()
            .Should().Contain("NOT EXISTS");
    }

    [Fact]
    public void The_publishers_cohort_id_query_compiles_when_scoped_to_partitions()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = SchedulePublisher
            .CohortIdsQuery(db, stageId: 40, academicYearId: 7, partitionLabels: ["A"])
            .ToQueryString();

        sql.Should().Contain("RotationGroup");
    }

    [Fact]
    public void The_publishers_published_cohort_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = SchedulePublisher.PublishedCohortIdsQuery(db, cohortIds: [1, 2]).ToQueryString();

        sql.Should().Contain("DISTINCT");
        sql.Should().Contain("EXISTS");
    }

    /// <summary>A correlated <c>Any()</c> inside the projection, rather than in the predicate.</summary>
    [Fact]
    public void The_publishers_candidate_assignment_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = SchedulePublisher.CandidateAssignmentsQuery(db, cohortIds: [1, 2]).ToQueryString();

        sql.Should().Contain("InternshipAssignments");
        sql.Should().Contain("EXISTS");
    }

    [Fact]
    public void The_publishers_slot_assignment_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = SchedulePublisher
            .SlotAssignmentsQuery(db, cohortIds: [1, 2], periodNumbers: null)
            .ToQueryString();

        sql.Should().Contain("CohortSlotAssignments");
        sql.Should().Contain("RotationMode");
    }

    /// <summary>
    /// The windowed form, which is what the macro plan publishes with: a concurrency block is a set
    /// of period numbers, so this is the shape a real publication takes.
    /// </summary>
    [Fact]
    public void The_publishers_slot_assignment_query_compiles_when_scoped_to_a_window()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = SchedulePublisher
            .SlotAssignmentsQuery(db, cohortIds: [1, 2], periodNumbers: [1, 2])
            .ToQueryString();

        sql.Should().Contain("PeriodNumber");
    }

    // ─── Chef worklist ────────────────────────────────────────────────────────
    //
    // Every slice of a chef worklist, and the four counts beside it, come out of one shared domain
    // predicate (ServicePeriodLifecycle). Two of the four reach through a one-to-one navigation that
    // has no row of its own when absent (p.Evaluation == null / != null), which is the shape a
    // provider is most likely to refuse — and this screen is the one nobody would notice was 500ing
    // until a chef said so. ⚠ The predicates being Expression<Func<>> handed to Where() rather than
    // written out inline is exactly what makes them worth compiling here: a plain method call in a
    // Where is refused by the provider, so this is what proves the shared form is the usable one.

    [Theory]
    [InlineData(ServicePeriodState.Planned)]
    [InlineData(ServicePeriodState.Underway)]
    [InlineData(ServicePeriodState.AwaitingEvaluation)]
    [InlineData(ServicePeriodState.Settled)]
    public void Every_chef_worklist_slice_compiles_to_sql(ServicePeriodState state)
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = GetMyServicePeriodsQueryHandler
            .OrderedScopedQuery(db, serviceIds: [45, 46], state)
            .ToQueryString();

        sql.Should().Contain("ServicePeriods");
        sql.Should().Contain("ORDER BY");
    }

    /// <summary>
    /// The search reaches through two navigations into the student and lower-cases both sides, which
    /// is the other shape on this query a provider could refuse.
    /// </summary>
    [Fact]
    public void The_searched_chef_worklist_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = GetMyServicePeriodsQueryHandler
            .OrderedScopedQuery(
                db, serviceIds: [45], ServicePeriodState.AwaitingEvaluation, searchTerm: "bennani")
            .ToQueryString();

        sql.Should().Contain("lower(");
        sql.Should().Contain("Users", "the student rows live in the shared Users table");
    }

    /// <summary>
    /// The year bound reaches the period through two navigations — assignment, then registration —
    /// which is the shape this file exists to compile. It is deliberately not a date comparison
    /// against the year's span: the schema states which year a period belongs to (three NOT NULL
    /// columns and a RESTRICT foreign key), and dates only ever approximated it.
    /// </summary>
    [Fact]
    public void The_year_scoped_chef_worklist_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = GetMyServicePeriodsQueryHandler
            .OrderedScopedQuery(db, serviceIds: [45], ServicePeriodState.Settled, academicYearId: 22)
            .ToQueryString();

        sql.Should().Contain("Registrations", "the year is read from the registration, not the dates");
        sql.Should().Contain("AcademicYearId");
    }

    /// <summary>
    /// The inscription import's identity lookup: four unique identifiers OR-ed together, each
    /// lower-cased on both sides. The shape is unusual enough to be worth compiling — a
    /// <c>Contains</c> over a projected, computed element is exactly what the provider refused in
    /// <c>CohortProvisioner</c> — and the stake here is higher than a 500: an identifier the query
    /// fails to match is a <b>second student row</b> carrying a value the unique index will then
    /// refuse.
    /// </summary>
    [Fact]
    public void The_inscription_identity_lookup_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = InscriptionPlanner
            .StudentsByIdentifierQuery(db, ["r130896"], ["ap2200a"], ["ab12345"], ["a@um5.ac.ma"])
            .ToQueryString();

        sql.Should().Contain("lower(", "every identifier is compared case-insensitively on both sides");
        sql.Should().Contain("Users", "the student rows live in the shared Users table");
    }

    [Fact]
    public void The_inscription_already_registered_lookup_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = InscriptionPlanner
            .RegisteredInYearQuery(db, academicYearId: 3, [Guid.NewGuid()])
            .ToQueryString();

        sql.Should().Contain("AcademicYearId");
    }

    /// <summary>
    /// <c>EndsWith</c> in a predicate is the kind of client-side-looking call this file exists to
    /// catch — in a <c>Where</c> the provider either translates it to LIKE or throws.
    /// </summary>
    [Fact]
    public void The_taken_address_lookup_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = InscriptionPlanner.TakenEmailsQuery(db, "um5.ac.ma").ToQueryString();

        sql.Should().Contain("Email");
    }

    /// <summary>
    /// The three scopes a teardown refusal counts over, and the two aggregates it counts with.
    /// </summary>
    /// <remarks>
    /// ⚠ The period counts fold an aggregate over a collection navigation. Written as extra columns
    /// on the affectation aggregate — one <c>GroupBy</c> carrying <c>g.Sum(a =&gt; a.ServicePeriods
    /// .Sum(p =&gt; p.Attendance.Count))</c> — that is a nested aggregate over an uncorrelatable
    /// element, the family of shape that killed the macro plan. Two flat queries is why there are two
    /// round trips, so this case is what holds that decision in place.
    /// </remarks>
    [Theory]
    [InlineData("roster")]
    [InlineData("year")]
    [InlineData("cohorts")]
    public void The_affectation_toll_queries_compile_to_sql(string scope)
    {
        using var db = TestHarness.NewNpgsqlContext();

        var assignments = scope switch
        {
            "roster"  => AffectationTollReader.AssignmentsOfRosterQuery(db, academicGroupId: 10),
            "year"    => AffectationTollReader.AssignmentsOfYearRostersQuery(db, academicYearId: 1),
            _         => AffectationTollReader.AssignmentsOfCohortsQuery(db, [1, 2, 3]),
        };

        AffectationTollReader.AssignmentCountsQuery(assignments).ToQueryString()
            .Should().ContainEquivalentOf("count(");

        AffectationTollReader.PeriodCountsQuery(assignments).ToQueryString()
            .Should().Contain("ServicePeriods");
    }

    /// <summary>
    /// The roll export. Flat by construction — the promotion, the roster, the statut and both CNPN
    /// stamps are reached by navigation, never by a collection folded inside the projection.
    /// </summary>
    [Fact]
    public void The_students_export_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = GetStudentsExportQueryHandler
            .RegistrationsQuery(db, yearId: 1, levelId: 3, program: null,
                academicGroupId: null, status: RegistrationStatus.Graduated, searchTerm: "ben")
            .ToQueryString();

        sql.Should().Contain("Registrations");
        sql.Should().Contain("AcademicGroups");
    }

    /// <summary>
    /// The three reads behind the stage export.
    /// </summary>
    /// <remarks>
    /// ⚠ The périodes of an attempt and the objective scores of an évaluation are both collections.
    /// Folded into the assignments projection — which is the obvious way to write « one row per
    /// stage, with its périodes » — that is a collection subquery in a projection, the exact family
    /// that killed the macro plan. Three flat queries joined in memory is why there are three round
    /// trips, and this case is what holds that decision in place.
    ///
    /// <para>The last two reach the scope through an <c>IN (subquery)</c> over the first, so this
    /// also pins that a subquery of a filtered <c>IQueryable</c> survives translation — the
    /// alternative being three restatements of the year predicate.</para>
    /// </remarks>
    [Fact]
    public void The_stage_export_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        StageAssignmentExportQueries
            .AssignmentsQuery(db, yearId: 1, levelId: 3, stageId: null,
                academicGroupId: null, onlyEvaluated: false)
            .ToQueryString()
            .Should().Contain("InternshipAssignments");

        StageAssignmentExportQueries
            .PeriodsQuery(db, yearId: 1, levelId: 3, stageId: null,
                academicGroupId: null, onlyEvaluated: false)
            .ToQueryString()
            .Should().Contain("ServicePeriods");

        StageAssignmentExportQueries
            .ObjectiveScoresQuery(db, yearId: 1, levelId: 3, stageId: null,
                academicGroupId: null, onlyEvaluated: true)
            .ToQueryString()
            .Should().Contain("ObjectiveScores");

        // The créneaux a période covers: a fourth flat read rather than a collection folded into the
        // périodes projection, which is the shape that took down the macro plan.
        StageAssignmentExportQueries
            .SlotCoverageQuery(db, yearId: 1, levelId: 3, stageId: null,
                academicGroupId: null, onlyEvaluated: false)
            .ToQueryString()
            .Should().Contain("ServicePeriodSlotCoverage");
    }

    /// <summary>
    /// Who leads a service, as the répartition and the stage export both read it.
    /// </summary>
    /// <remarks>
    /// ⚠ The tenures are deliberately <b>not</b> projected inside the services query. A tenure
    /// becomes a computed element with no key of its own, and a collection of those inside a
    /// <c>Select</c> is exactly « Unable to translate a collection subquery in a projection ». Two
    /// top-level reads keyed on the service instead — this pins that they stay that way.
    /// </remarks>
    [Fact]
    public void The_service_chef_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        ServiceChefProvider.ServicesQuery(db, [1, 2, 3])
            .ToQueryString()
            .Should().Contain("Services");

        ServiceChefProvider.TenuresQuery(db, [1, 2, 3])
            .ToQueryString()
            .Should().Contain("ServiceChefAssignment");
    }

    /// <summary>
    /// The four reads behind the planning grid, now that its rows are paged.
    /// </summary>
    /// <remarks>
    /// ⚠ Two of them end in <c>Distinct()</c> over a <b>computed element</b> — a record built in the
    /// projection rather than an entity carrying a key — which is the same family as the collection
    /// subquery that took the macro plan down. They exist precisely so the saturation report and the
    /// partition warning stay whole while the rows are paged, so a provider refusing them would take
    /// the grid with it.
    ///
    /// <para><c>PartitionSlotUseQuery</c> additionally projects a <b>nullable</b> string
    /// (<c>RotationGroup</c>) into a record — the promotion nobody has cut is the ordinary case, and
    /// a null there must survive the round trip rather than become a refusal.</para>
    /// </remarks>
    [Fact]
    public void The_planning_grid_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        GetStageScheduleQueryHandler.SlotsQuery(db, stageId: 1, academicYearId: 1)
            .ToQueryString().Should().Contain("StageSlots");

        GetStageScheduleQueryHandler.ScopedCohortsQuery(db, stageId: 1, academicYearId: 1, rotationGroup: "A")
            .ToQueryString().Should().Contain("Cohorts");

        GetStageScheduleQueryHandler.ScopedCellPairsQuery(db, stageId: 1, academicYearId: 1, rotationGroup: null)
            .ToQueryString().Should().Contain("DISTINCT");

        GetStageScheduleQueryHandler.PageCellsQuery(db, [1, 2, 3])
            .ToQueryString().Should().Contain("CohortSlotAssignments");

        GetStageScheduleQueryHandler.PartitionsQuery(db, stageId: 1, academicYearId: 1)
            .ToQueryString().Should().Contain("GROUP BY");

        GetStageScheduleQueryHandler.PartitionSlotUseQuery(db, stageId: 1, academicYearId: 1)
            .ToQueryString().Should().Contain("DISTINCT");
    }

    /// <summary>
    /// The columns each cohorte of the list stands in.
    /// </summary>
    /// <remarks>
    /// ⚠ Written the obvious way — <c>c.SlotAssignments.Select(a =&gt; a.StageSlot.PeriodNumber)</c>
    /// inside the row projection — the element is a computed <c>int</c> with no key, which is the
    /// family Npgsql refuses. This case pins the flat form that replaced it.
    /// </remarks>
    [Fact]
    public void The_cohort_lists_period_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        GetCohortByStageIdQueryHandler.PeriodNumbersQuery(db, [1, 2, 3])
            .ToQueryString().Should().Contain("DISTINCT");
    }

    /// <summary>
    /// The affectation's candidate read, now that it is asked once for every cohorte of a call
    /// instead of once per cohorte.
    /// </summary>
    /// <remarks>
    /// ⚠ It dereferences a nullable FK in the projection (<c>r.AcademicGroupId!.Value</c>) after
    /// testing it in the predicate. The compiler is satisfied by the <c>!</c>; whether the provider
    /// is, is a fact about the provider — and this query is on the macro-plan path, where the last
    /// untranslatable projection cost the whole 6ᵉ année.
    /// </remarks>
    [Fact]
    public void The_student_affectation_candidate_query_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = StudentAffectationService
            .EligibleRegistrationsQuery(db, [1, 2, 3], [4])
            .ToQueryString();

        sql.Should().Contain("Registrations");
        sql.Should().Contain("AcademicGroupId");
    }

    // =============================================================================================
    // The CNPN — which text governs whom
    // =============================================================================================
    //
    // ⚠ This area had no case at all until 2026-09-01, on the strength of its queries looking flat.
    // « Looks flat » is what was believed about CohortProvisioner too. It is also the area with the
    // least forgiving failure: the stamper runs inside the réinscription, which creates a whole
    // promotion's registrations in one act, so an untranslatable read there fails the rollover
    // rather than one screen.

    /// <summary>
    /// The stamper's four reads. Every one is deliberately flat — read, then fold in memory — because
    /// folding the grouping into the projection is the collection-subquery shape the provider
    /// refuses. Compiling them is what says the shape is still flat.
    /// </summary>
    [Fact]
    public void The_stampers_reads_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();
        var students = new[] { Guid.NewGuid() };
        var pending = new[] { Guid.NewGuid() };

        RegistrationCnpnStamper.AcademicYearsQuery(db)
            .ToQueryString().Should().Contain("AcademicYears");

        RegistrationCnpnStamper.PriorStampsQuery(db, students, pending)
            .ToQueryString().Should().Contain("CnpnVersionId");

        RegistrationCnpnStamper.PriorRegistrationsQuery(db, students, pending)
            .ToQueryString().Should().Contain("Registrations");

        RegistrationCnpnStamper.StampProgramsQuery(db, [1, 2])
            .ToQueryString().Should().Contain("CnpnVersions");
    }

    /// <summary>
    /// The effectivity rules, joined to the year they start in. Compared on <c>StartDate</c> rather
    /// than on year ids — « la règle en vigueur » is the latest one at or before the registration's
    /// year, and ids carry no order — so the projection reaches through a navigation.
    /// </summary>
    [Fact]
    public void The_effectivity_rule_lookup_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = RegistrationCnpnStamper.EffectivityRulesQuery(db, [1, 2, 3]).ToQueryString();

        sql.Should().Contain("CnpnLevelEffectivities");
        sql.Should().Contain("StartDate");
    }

    [Fact]
    public void The_effectivity_planners_scope_and_detail_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        CnpnEffectivityPlanner
            .InScopeQuery(db, levelId: 3, from: new DateOnly(2026, 9, 1), nextFrom: null)
            .ToQueryString().Should().Contain("Registrations");

        // The open-ended form and the bounded one compile separately: the second parameter reaches
        // the predicate as a nullable compared against a navigation's column.
        CnpnEffectivityPlanner
            .InScopeQuery(db, levelId: 3, from: new DateOnly(2026, 9, 1), nextFrom: new DateOnly(2028, 9, 1))
            .ToQueryString().Should().Contain("Registrations");

        // Joins to Users, not to a "Students" table: Student is a TPH discriminator on Users, which
        // is the sort of thing an assertion naming the C# type would quietly get wrong.
        CnpnEffectivityPlanner.RowDetailQuery(db, [Guid.NewGuid()])
            .ToQueryString().Should().Contain("Users");
    }

    /// <summary>
    /// ⚠ The targeting selector is composed into three other queries as a subquery, which is the
    /// shape that actually needed pinning: it must become <c>IN (SELECT …)</c> rather than a
    /// materialised list of Guids. A promotion is 833 students on the live base, and the rule this
    /// codebase follows is that a set which is <i>described</i> reaches the store as a predicate.
    /// </summary>
    [Fact]
    public void The_targeting_selector_compiles_as_a_subquery()
    {
        using var db = TestHarness.NewNpgsqlContext();

        var matched = CnpnTargetPlanner.MatchedStudentIdsQuery(
            db, AcademicProgram.Medecine, asOfYearId: 1, maxLevelYear: 2);

        matched.ToQueryString().Should().Contain("DISTINCT");

        string composed = db.Students.Where(s => matched.Contains(s.Id)).ToQueryString();

        composed.Should().Contain("SELECT", "the selector must ride along as a subquery");
        composed.Should().Contain("Registrations", "…not be replaced by ids fetched beforehand");
    }

    /// <summary>
    /// Both CNPN read screens carry a correlated <c>Count</c> in the projection. A scalar subquery is
    /// translatable where a collection subquery over a computed element is not, and the distinction
    /// is exactly the one this file exists to hold on to.
    /// </summary>
    [Fact]
    public void The_cnpn_read_screens_counts_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        GetCnpnVersionsQueryHandler.VersionRowsQuery(db, program: null)
            .ToQueryString().Should().Contain("CnpnVersions");

        GetCnpnVersionsQueryHandler.VersionRowsQuery(db, AcademicProgram.Medecine)
            .ToQueryString().Should().Contain("CnpnVersions");

        GetCnpnEffectivitiesQueryHandler.EffectivityRowsQuery(db, cnpnVersionId: null, program: null)
            .ToQueryString().Should().Contain("CnpnLevelEffectivities");
    }

    /// <summary>
    /// The réinscription roll's five reads.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>This act had no coverage here at all before it existed, and it is the least
    /// forgiving place to skip it:</b> one upload closes a year for 6 800 students and opens the next
    /// for them, so a query the provider refuses is a 500 on the single most consequential button in
    /// the application — and the whole file is refused with it.</para>
    ///
    /// <para>Two shapes are worth naming. <c>StudentsByCodeQuery</c> puts a <c>Contains</c> over a
    /// listed set of ~6 800 strings in the predicate, which Npgsql must render as one array parameter
    /// rather than 6 800 of them. And the two text lookups project through a required navigation
    /// (<c>CnpnVersion!.TotalYears</c>) behind a null check — a left join the provider has to see
    /// through, and the reason they are two flat queries rather than one projection carrying both.</para>
    /// </remarks>
    [Fact]
    public void The_reinscription_roll_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        ReinscriptionSheetPlanner.LevelCatalogueQuery(db)
            .ToQueryString().Should().Contain("Levels");

        // The identifiers the file names. `= ANY (@__codes_0)` is the shape that keeps a 6 862-row
        // roll to a single parameter; a per-code parameter list would not survive the real file.
        string byCode = ReinscriptionSheetPlanner
            .StudentsByCodeQuery(db, ["24008386", "25019590"])
            .ToQueryString();

        byCode.Should().Contain("Users", "Student is a TPH discriminator on Users, not its own table");
        byCode.Should().Contain("ANY", "the codes must ride along as one array parameter");

        ReinscriptionSheetPlanner.AlreadyRegisteredQuery(db, toAcademicYearId: 22, [Guid.NewGuid()])
            .ToQueryString().Should().Contain("Registrations");

        // The final-year gate's « is he beginning this level or continuing in it » read.
        FinalYearGuard.AlreadyRegisteredAtLevelQuery(db, [Guid.NewGuid()], levelId: 7)
            .ToQueryString().Should().Contain("DISTINCT");

        // ⚠ The two halves of « how long does his text run ». Kept apart deliberately: folded into
        // one projection the registration's text has to be reached through a filtered navigation,
        // which is the family of shape that killed the macro plan.
        ReinscriptionSheetPlanner.StudentTextQuery(db, [Guid.NewGuid()])
            .ToQueryString().Should().Contain("CnpnVersions");

        // ⚠ Predicate-scoped, not id-listed: the closing year is 8 077 registrations and the
        // absentee pass needs every one of their texts, not just those the file names.
        ReinscriptionSheetPlanner.ClosingYearTextQuery(db, fromAcademicYearId: 21)
            .ToQueryString().Should().Contain("CnpnVersions");
    }

    /// <summary>
    /// The CNPN attribution pass reads every registration in the base — 49 500 of them — through two
    /// navigations at once, to get the year it sits in and the level year it names. It is the widest
    /// unfiltered read in the application, and it runs exactly once: on a database being rebuilt,
    /// where a translation failure means the import finishes and nobody is stamped.
    /// </summary>
    [Fact]
    public void The_cnpn_attribution_read_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = CnpnHistoryAttributor.EnrolmentsQuery(db).ToQueryString();

        sql.Should().Contain("Registrations");
        sql.Should().Contain("AcademicYears", "the year's StartDate is what entry is walked back through");
        sql.Should().Contain("Levels", "the level year is what says whether the entry is recorded");
    }

    /// <summary>
    /// The hold exclusion is a collection aggregate, and this is the case that says which side of the
    /// line it falls on.
    ///
    /// <para>⚠ <b>A collection in a <em>predicate</em> is an <c>EXISTS</c> and translates; the same
    /// collection in a <em>projection</em> is the shape Npgsql refuses</b> — the family that killed
    /// the macro plan. <c>RegistrationHoldPolicy.Plannable</c> is composed into the two hottest
    /// planning reads in the application, so if it did not compile, cohort affectation would 500 on
    /// the first real « Générer le plan » with the whole suite green.</para>
    /// </summary>
    [Fact]
    public void The_registration_hold_exclusion_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string affectation = StudentAffectationService
            .EligibleRegistrationsQuery(db, [1, 2], [3])
            .ToQueryString();

        affectation.Should().Contain("EXISTS", "the unreleased-hold test is an EXISTS, not a subquery projection");
        affectation.Should().Contain("RegistrationHolds");

        string texts = CohortProvisioner.GroupTextsQuery(db, [1, 2]).ToQueryString();

        texts.Should().Contain("RegistrationHolds",
            "a held registration does not decide which texts its roster follows");
    }

    /// <summary>
    /// The address allocator's lookup. It runs on the réinscription apply, once, for a file that
    /// names students PGSH does not hold — and if it did not compile, the roll would 500 at the
    /// moment it is applied to a whole promotion.
    /// </summary>
    [Fact]
    public void The_taken_email_lookup_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = ReinscriptionSheetPlanner.TakenEmailsQuery(db, "um5.ac.ma").ToQueryString();

        sql.Should().Contain("Users");
        sql.Should().Contain("LIKE", "the domain filter is a suffix match, pushed to the server");
    }

    /// <summary>
    /// ⚠ The hold exclusion now filters on the <b>reason</b> as well, through a static array. EF
    /// translates <c>Contains</c> over one into an <c>IN</c>; if it did not, every planning read
    /// would throw on the first real request with the whole suite green.
    /// </summary>
    [Fact]
    public void The_blocking_reason_filter_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = StudentAffectationService
            .EligibleRegistrationsQuery(db, [1, 2], [3])
            .ToQueryString();

        sql.Should().Contain("EXISTS");
        sql.Should().Contain("RegistrationHolds");
        sql.Should().Contain("Reason", "the advisory reasons must not exclude anybody from planning");
    }

    /// <summary>
    /// The signalements worklist. Year-scoped through the <b>registration's</b> own
    /// <c>AcademicYearId</c> rather than through <c>RaisedOn</c> — one roll raises holds on the
    /// closing year's registrations and creates the opening year's in the same act, so the date the
    /// flag was written says nothing about which promotion it belongs to.
    /// </summary>
    [Fact]
    public void The_registration_holds_worklist_compiles_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string sql = GetRegistrationHoldsQueryHandler
            .ScopedQuery(db, academicYearId: 21, reason: null, RegistrationHoldFilter.Active)
            .ToQueryString();

        sql.Should().Contain("RegistrationHolds");
        sql.Should().Contain("Registrations", "the year is read through the registration, not off the hold");
    }

    /// <summary>
    /// The three reads behind the cross-service occupancy report.
    ///
    /// <para>⚠ <c>PlacementsQuery</c> is the one worth pinning. It projects the cohort's assignment
    /// <b>count</b> — an aggregate over a collection navigation, which translates — where a
    /// projected collection of those assignments would be the element with no key that Npgsql
    /// refuses. Same family as the shape that killed the macro plan, and this one runs over every
    /// service at once.</para>
    /// </summary>
    [Fact]
    public void The_occupancy_report_queries_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string placements = GetOccupancyReportQueryHandler
            .PlacementsQuery(db, new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31))
            .ToQueryString();

        placements.Should().Contain("CohortSlotAssignments");
        placements.Should().Contain("StageSlots", "the window is the slot's, and it is what bounds the year");
        placements.Should().ContainEquivalentOf("count(", "the load is the cohort's affectation count");

        GetOccupancyReportQueryHandler.ServicesQuery(db, hospitalId: 3)
            .ToQueryString().Should().Contain("Services");

        // The denominator of « il en utilise deux sur cinq » — a correlated count, not a collection.
        GetOccupancyReportQueryHandler.AllowedServicesQuery(db)
            .ToQueryString().Should().Contain("StageAllowedServices");
    }

    /// <summary>
    /// The two reads behind « quel groupe va déjà là ? » and « cet hôpital peut-il l'accueillir ? ».
    ///
    /// <para>⚠ Worth pinning for three separate reasons. <c>MatchingRostersQuery</c> nests an
    /// aggregate two navigations deep (roster → cohortes → cellules) and, under
    /// <c>Exclusively</c>, negates one of them — an <c>EXISTS</c>/<c>NOT EXISTS</c> pair that only a
    /// compile can confirm. <c>PageCellsQuery</c> reaches a second level of navigation in its
    /// projection (<c>a.Service.Hospital.Name</c>). And <c>ServicesAtHospitalQuery</c> is a
    /// <c>SelectMany</c> over a collection navigation projecting a keyless record — one step away
    /// from the collection-in-a-projection shape Npgsql refuses, which is exactly why it is written
    /// flat rather than folded into the row beside it.</para>
    /// </summary>
    [Fact]
    public void The_placement_reads_compile_to_sql()
    {
        using var db = TestHarness.NewNpgsqlContext();

        string byService = GetRosterPlacementsQueryHandler
            .MatchingRostersQuery(db, academicYearId: 21, levelId: 3, stageId: 7,
                serviceId: 11, hospitalId: null, PlacementMatch.Anywhere)
            .ToQueryString();

        byService.Should().Contain("AcademicGroups");
        byService.Should().Contain("CohortSlotAssignments", "the placement lives on the cell");

        string exclusively = GetRosterPlacementsQueryHandler
            .MatchingRostersQuery(db, academicYearId: 21, levelId: 3, stageId: null,
                serviceId: null, hospitalId: 2, PlacementMatch.Exclusively)
            .ToQueryString();

        exclusively.Should().ContainEquivalentOf("NOT EXISTS",
            "« aucune cellule ailleurs » is the negative half of Exclusively");
        exclusively.Should().ContainEquivalentOf("EXISTS",
            "and « au moins une cellule » is the positive half that keeps an unarranged roster out");

        GetRosterPlacementsQueryHandler.PageStagesQuery(db, [1, 2, 3], stageId: null)
            .ToQueryString().Should().Contain("Cohorts");

        GetRosterPlacementsQueryHandler.PageCellsQuery(db, [1, 2, 3], stageId: null)
            .ToQueryString().Should().Contain("Hospitals", "the row names the hospital, two hops out");

        GetRosterPlacementsQueryHandler.PromotionStagesQuery(db, academicYearId: 21, levelId: 3)
            .ToQueryString().Should().Contain("Cohorts");

        // The two counts are correlated aggregates over a navigation, which translate; the services
        // themselves are a second flat read for the reason above.
        GetHospitalStageCoverageQueryHandler.StagesQuery(db, levelId: 3, hospitalId: 2)
            .ToQueryString().Should().ContainEquivalentOf("count(");

        GetHospitalStageCoverageQueryHandler.ServicesAtHospitalQuery(db, levelId: 3)
            .ToQueryString().Should().Contain("StageAllowedServices",
                "the names are loaded through the join table; only the verdict comes from the counts");
    }
}
