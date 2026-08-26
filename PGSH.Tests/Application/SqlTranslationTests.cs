using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Planning;
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
}
