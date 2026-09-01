using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>GET employees/me/service-periods</c> through the real pipeline.
///
/// <para>Everything interesting here is binding, and none of it is visible to a handler test: the
/// query record is bound from the query string with <c>[AsParameters]</c>, the slice is an enum
/// travelling as a string, and both defaults that matter are decided by the pipeline rather than by
/// the handler. Two failures this pins, each of which would look like data rather than like a
/// bug:</para>
/// <list type="bullet">
///   <item>an omitted slice resolving to <c>default(ServicePeriodState)</c> — value 0, i.e.
///   <c>Upcoming</c> — would make the chef's normal landing state the one slice he can act on
///   nothing in;</item>
///   <item>an omitted or zero page size reaching <c>ToPaginatedResponseAsync</c>, which clamps a 0
///   <i>upward</i> to 1 — a three-student service answering with one student, and nothing anywhere
///   saying so.</item>
/// </list>
/// <para>Measured while writing these: <c>[AsParameters]</c> <em>does</em> honour a declared default
/// on .NET 9, so the first is not live today — these tests are what would catch it changing, and
/// what justifies the nullable parameters that make the fallback the handler's own choice. The
/// second is live for any caller that sends <c>?pageSize=0</c>.</para>
/// </summary>
public class ChefWorklistEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int ChefService = 1;
    private const int PreviousYear = 99;
    private const string Route = "/api/employees/me/service-periods";

    private static readonly Guid ChefIdentity = Guid.Parse("11112222-3333-4444-5555-666677778888");
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End = new(2026, 3, 31);

    // The API registers JsonStringEnumConverter globally, so the slice travels as "Current", not 1.
    // Reading it back the same way is part of what these tests assert about the wire.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;

    public ChefWorklistEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// One service the caller leads, holding three live rotations and two the administration has
    /// published but not opened. Three is the number that matters: a page silently clamped to one row
    /// is indistinguishable from a correct page whenever the slice holds only one.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity, "chef.integration@pgsh.ma");
        var service = db.SeedService(ChefService, "Pédiatrie", chef);
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        Add("Sara", "Bennani", started: true);
        Add("Ali", "Amrani", started: true);
        Add("Nadia", "Fassi", started: true);
        Add("Omar", "Tazi", started: false);
        Add("Hind", "Berrada", started: false);

        void Add(string firstName, string lastName, bool started)
        {
            var registration = db.SeedRegistration(firstName, lastName, cohort.AcademicGroup);
            var assignment = db.SeedAssignment(registration, cohort);
            db.SeedPeriod(assignment, service, Start, End, started);
        }
    });

    private HttpClient Chef() => _factory.CreateApiClient(ChefIdentity);

    private static async Task<ChefWorklistResponse> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ChefWorklistResponse>(Json))!;
    }

    // ⚠ Value 0 of the enum is Upcoming, so anything that resolves an absent slice to default(T)
    // lands the chef on rotations he cannot act on and calls it his worklist.
    [Fact]
    public async Task Omitting_the_slice_lands_on_the_live_one_not_on_value_zero()
    {
        var response = await ReadAsync(await Chef().GetAsync($"{Route}?serviceId={ChefService}"));

        response.State.Should().Be(ServicePeriodState.Underway);
        response.Page.Items.Should().OnlyContain(p => p.State == ServicePeriodState.Underway);
    }

    [Fact]
    public async Task Omitting_the_page_size_returns_the_whole_slice_not_a_single_row()
    {
        var response = await ReadAsync(await Chef().GetAsync($"{Route}?serviceId={ChefService}"));

        response.Page.Items.Should().HaveCount(3);
        response.Page.PageSize.Should().Be(GetMyServicePeriodsQuery.DefaultPageSize);
    }

    [Fact]
    public async Task The_slice_binds_from_the_query_string_by_name()
    {
        var response = await ReadAsync(
            await Chef().GetAsync($"{Route}?serviceId={ChefService}&state=Planned"));

        response.State.Should().Be(ServicePeriodState.Planned);
        response.Page.Items.Should().HaveCount(2)
            .And.OnlyContain(p => p.State == ServicePeriodState.Planned);
    }

    // The control: without it, a route that 400s or empties on everything would satisfy every
    // assertion above about what a slice excludes.
    [Fact]
    public async Task Every_slice_is_reachable_and_together_they_cover_the_service()
    {
        var client = Chef();

        var upcoming = await ReadAsync(await client.GetAsync($"{Route}?serviceId={ChefService}&state=Planned"));
        var current = await ReadAsync(await client.GetAsync($"{Route}?serviceId={ChefService}&state=Underway"));
        var toEvaluate = await ReadAsync(await client.GetAsync($"{Route}?serviceId={ChefService}&state=AwaitingEvaluation"));
        var history = await ReadAsync(await client.GetAsync($"{Route}?serviceId={ChefService}&state=Settled"));

        upcoming.Page.TotalCount.Should().Be(2);
        current.Page.TotalCount.Should().Be(3);
        toEvaluate.Page.TotalCount.Should().Be(0);
        history.Page.TotalCount.Should().Be(0);
        current.Counts.Total.Should().Be(5, "the four slices partition the service");
    }

    // The counts are what lets the client open on a slice that has work in it, so they have to
    // survive serialisation whichever slice was asked for.
    [Fact]
    public async Task The_counts_come_back_on_every_slice_including_an_empty_one()
    {
        var response = await ReadAsync(
            await Chef().GetAsync($"{Route}?serviceId={ChefService}&state=Settled"));

        response.Page.Items.Should().BeEmpty();
        response.Counts.Planned.Should().Be(2);
        response.Counts.Underway.Should().Be(3);
    }

    [Fact]
    public async Task The_search_binds_and_narrows_both_the_rows_and_the_counts()
    {
        var response = await ReadAsync(
            await Chef().GetAsync($"{Route}?serviceId={ChefService}&searchTerm=bennani"));

        response.Page.Items.Should().ContainSingle()
            .Which.StudentFullName.Should().Contain("Bennani");
        response.Counts.Total.Should().Be(1);
    }

    // ⚠ The live case of the clamp: ToPaginatedResponseAsync raises a 0 to 1, so a zero page size
    // has to be read as "unstated" here or it silently becomes a worklist of one student.
    [Fact]
    public async Task An_explicit_zero_page_size_is_read_as_unstated_not_as_one_row()
    {
        var response = await ReadAsync(
            await Chef().GetAsync($"{Route}?serviceId={ChefService}&pageSize=0&pageNumber=0"));

        response.Page.Items.Should().HaveCount(3);
        response.Page.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task The_page_size_binds_and_the_total_still_names_what_is_reachable()
    {
        var response = await ReadAsync(
            await Chef().GetAsync($"{Route}?serviceId={ChefService}&pageSize=2&pageNumber=2"));

        response.Page.Items.Should().ContainSingle();
        response.Page.TotalCount.Should().Be(3);
        response.Page.PageNumber.Should().Be(2);
    }

    // A slice name that is not one is a caller error, not a silent fallback onto some other slice.
    [Fact]
    public async Task An_unknown_slice_name_is_refused_rather_than_quietly_reinterpreted()
    {
        var response = await Chef().GetAsync($"{Route}?serviceId={ChefService}&state=Whatever");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── The year filter ──────────────────────────────────────────────────────

    // The year is resolved server-side when the caller omits it, so the response has to name the one
    // it chose: a selector that had to work out which year it is showing would be a second place for
    // that answer to live, and the two would eventually disagree.
    [Fact]
    public async Task An_omitted_year_resolves_to_the_current_one_and_comes_back_named()
    {
        var response = await ReadAsync(await Chef().GetAsync($"{Route}?serviceId={ChefService}"));

        response.AcademicYearId.Should().Be(TestHarness.CurrentYearId);
        response.OutsideYearCount.Should().Be(0);
        response.Page.Items.Should().HaveCount(3, "the seeded rotations run inside that year");
    }

    // ⚠ A bool through [AsParameters]. If this ever stops binding, the year filter loses its escape
    // hatch while still hiding rows — which is the shape of the incident it was built to prevent.
    [Fact]
    public async Task AllYears_binds_from_the_query_string_and_removes_the_year_bound()
    {
        var response = await ReadAsync(
            await Chef().GetAsync($"{Route}?serviceId={ChefService}&allYears=true"));

        response.AcademicYearId.Should().BeNull();
        response.OutsideYearCount.Should().Be(0, "nothing is outside a read that spans everything");
        response.Page.Items.Should().HaveCount(3);
    }

    // The pair that matters, end to end: a rotation the default view legitimately hides is counted
    // where the chef can see the number, and one parameter brings it back.
    //
    // Note the seed: the extra student is registered in an EARLIER year while his rotation runs on
    // exactly the same dates as the other three. That is the whole point of scoping on the
    // registration — the dates cannot tell the two apart, and the schema never had to.
    [Fact]
    public async Task A_rotation_of_another_year_is_counted_and_reachable()
    {
        await _factory.SeedAsync(db =>
        {
            db.SeedAcademicYear(
                PreviousYear, "2024-2025", new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31));

            var service = db.Services.Single(x => x.Id == ChefService);
            var cohort = db.Cohorts.Single();
            var registration = db.SeedRegistration(
                "Youssef", "Idrissi", cohort.AcademicGroup, PreviousYear);
            var assignment = db.SeedAssignment(registration, cohort);
            db.SeedPeriod(assignment, service, Start, End);
        });

        var client = Chef();

        var scoped = await ReadAsync(await client.GetAsync($"{Route}?serviceId={ChefService}"));
        var everything = await ReadAsync(
            await client.GetAsync($"{Route}?serviceId={ChefService}&allYears=true"));
        var previous = await ReadAsync(
            await client.GetAsync($"{Route}?serviceId={ChefService}&academicYearId={PreviousYear}"));

        scoped.Page.Items.Should().HaveCount(3, "the fourth is registered in 2024-2025");
        scoped.OutsideYearCount.Should().Be(1, "and the chef is told so rather than left guessing");
        everything.Page.Items.Should().HaveCount(4);
        previous.Page.Items.Should().ContainSingle("and the year he belongs to lists him");
    }

    // A chef's worklist is his identity's, so an unauthenticated caller must not reach it at all —
    // and a handler that always sees a caller cannot tell "allowed" from "never checked".
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync($"{Route}?serviceId={ChefService}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The read must write nothing. Trivially true today, and the assertion is what keeps it so.
    [Fact]
    public async Task Reading_the_worklist_leaves_the_store_untouched()
    {
        await Chef().GetAsync($"{Route}?serviceId={ChefService}&state=Planned");

        var started = await _factory.QueryAsync<int>(async (ApplicationDbContext db) =>
            db.ServicePeriods.Count(p => p.IsStarted));

        started.Should().Be(3, "listing a planned rotation must never open it");
    }
}
