using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
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
}
