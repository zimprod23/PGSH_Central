using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Stages.Schedule;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>GET stages/{id}/schedule</c> through the real pipeline, now that its rows are paged.
///
/// <para>Everything pinned here is binding or shape, and none of it is visible to a handler test.
/// Three failures it catches, each of which would read as data rather than as a bug:</para>
/// <list type="bullet">
///   <item><c>?pageSize=0</c> reaching <c>ToPaginatedResponseAsync</c>, which clamps a zero
///   <i>upward</i> to 1 — a promotion answering with one cohorte and nothing saying so;</item>
///   <item>the four new query parameters not binding at all, which on optional primitives is silent:
///   the response is simply page 1 of everything, i.e. exactly what the change was meant to stop;</item>
///   <item>the summary failing to serialise — the counts and the saturation report are what every
///   number on that screen is now read from, and an absent <c>summary</c> is an empty grid.</item>
/// </list>
/// </summary>
public class StageScheduleEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int ServiceId = 1;
    private const int Rosters = 6;

    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End = new(2026, 3, 31);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;

    public StageScheduleEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Six rosters on one column, alternating between two partitions. Six is the number that matters:
    /// a page silently clamped to one row is indistinguishable from a correct page whenever the
    /// selection holds one.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie");
        var slot = db.SeedSlot(stage, 100, 1, Start, End);

        for (int i = 1; i <= Rosters; i++)
        {
            var group = db.SeedGroup(i, i, rotationGroup: i % 2 == 1 ? "A" : "B");
            var cohort = db.SeedCohortFor(stage, group, i);
            db.SeedAssignment(db.SeedRegistration($"E{i}", "Test", group), cohort);
            db.SeedSlotAssignment(i, cohort, slot, service);
        }
    });

    // ApiFactory seeds one calling user; CreateApiClient() with no identity authenticates as it.
    // Passing an unseeded subject 403s in SyncUserMiddleware, which is a profile problem, not a
    // permission one — and would make every assertion below pass for the wrong reason.
    private HttpClient Client() => _factory.CreateApiClient(null, Roles.Scolarite);

    private static async Task<StageScheduleResponse> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<StageScheduleResponse>(Json))!;
    }

    private static string Route(string? query = null) =>
        $"/api/stages/{TestHarness.StageId}/schedule" + (query is null ? "" : $"?{query}");

    // The control: without it, a route that 400s or empties on everything would satisfy every
    // assertion below about what a page excludes.
    [Fact]
    public async Task The_grid_comes_back_whole_when_nothing_is_asked_of_it()
    {
        var response = await ReadAsync(await Client().GetAsync(Route()));

        response.Slots.Should().ContainSingle();
        response.Cohorts.Items.Should().HaveCount(Rosters);
        response.Cohorts.TotalCount.Should().Be(Rosters);
        response.Cohorts.PageSize.Should().Be(GetStageScheduleQuery.DefaultPageSize);
    }

    [Fact]
    public async Task The_paging_parameters_bind_from_the_query_string()
    {
        var response = await ReadAsync(await Client().GetAsync(Route("pageSize=2&pageNumber=2")));

        response.Cohorts.Items.Should().HaveCount(2);
        response.Cohorts.PageNumber.Should().Be(2);
        response.Cohorts.TotalCount.Should().Be(Rosters, "the total names what is reachable, not what is shown");
    }

    // ⚠ The live case of the clamp: ToPaginatedResponseAsync raises a 0 to 1, so a zero has to be read
    // as "unstated" or a promotion of a hundred answers with a single cohorte.
    [Fact]
    public async Task An_explicit_zero_page_size_is_read_as_unstated_not_as_one_row()
    {
        var response = await ReadAsync(await Client().GetAsync(Route("pageSize=0&pageNumber=0")));

        response.Cohorts.Items.Should().HaveCount(Rosters);
        response.Cohorts.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task The_partition_filter_binds_and_narrows_the_rows()
    {
        var response = await ReadAsync(await Client().GetAsync(Route("rotationGroup=A")));

        response.Cohorts.Items.Should().HaveCount(3)
            .And.OnlyContain(c => c.RotationGroup == "A");
        response.Summary.TotalCohorts.Should().Be(3);
    }

    /// <summary>
    /// The whole point of the summary: it survives the page, and it is what the screen's numbers and
    /// its two derived warnings are read from. An absent or empty one is an empty grid.
    /// </summary>
    [Fact]
    public async Task The_summary_travels_with_every_page_and_describes_the_selection()
    {
        var response = await ReadAsync(await Client().GetAsync(Route("pageSize=1&pageNumber=3")));

        response.Cohorts.Items.Should().ContainSingle();
        response.Summary.TotalCohorts.Should().Be(Rosters);
        response.Summary.ConfiguredUnpublishedCohorts.Should().Be(
            Rosters, "this is the number « Publier tout » acts on, and it is not the page size");
        response.Summary.Partitions.Select(p => p.Label).Should().BeEquivalentTo(["A", "B"]);
        response.Summary.OccupiedSlotIds.Should().ContainSingle();
        response.Summary.PartitionUsage.Should().HaveCount(2, "both partitions stand in the one column");
    }

    /// <summary>
    /// ⚠ The partitions are the chips the user filters *with*. Narrowed by the active filter there is
    /// no way back to the others, so a filtered response must still name every one of them.
    /// </summary>
    [Fact]
    public async Task Filtering_to_one_partition_still_names_the_others()
    {
        var response = await ReadAsync(await Client().GetAsync(Route("rotationGroup=B&pageSize=1")));

        response.Summary.Partitions.Select(p => p.Label).Should().BeEquivalentTo(["A", "B"]);
        response.Summary.PartitionUsage.Should().Contain(u => u.RotationGroup == "A");
    }

    [Fact]
    public async Task An_unknown_stage_is_refused_rather_than_answered_with_an_empty_grid()
    {
        var response = await Client().GetAsync("/api/stages/999999/schedule");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Sending no header leaves the request anonymous — a route that always authenticates cannot tell
    // "allowed" from "not checked".
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var response = await _factory.CreateClient().GetAsync(Route());

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
