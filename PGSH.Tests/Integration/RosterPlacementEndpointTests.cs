using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using PGSH.Application.AcademicGroups.Placements;
using PGSH.Application.Hospitals.Coverage;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>GET groups/placements</c> and <c>GET hospitals/{id}/stage-coverage</c> through the real
/// pipeline.
///
/// <para>Everything here is invisible to a handler test. The query is bound from the query string
/// with <c>[AsParameters]</c>, so <c>match=Exclusively</c> has to survive travelling as a string —
/// and if it does not, it falls to <c>default</c>, which is <c>Anywhere</c>: the search silently
/// answers the weaker question and returns rosters that only <i>partly</i> go where the student must
/// go. Nothing on the response would say so.</para>
///
/// <para>⚠ And the three validator rules exist <b>only</b> in the pipeline.
/// <c>ValidationPipelineBehavior</c> runs them, so a test calling the handler passes a contradictory
/// query straight through — the same blind spot that let <c>UpdateStageCommandValidator</c> make the
/// whole stage catalogue read-only with every handler test green.</para>
/// </summary>
public class RosterPlacementEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int Militaire = 2;
    private const int MilitaryService = 2;
    private const int CivilService = 1;
    private const int MilitaryRoster = 1;
    private const int UnarrangedRoster = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;

    public RosterPlacementEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// One roster entirely at the military hospital and one nobody has arranged. Two is the number
    /// that matters: the second is what a <c>match</c> lost in binding would hand back as an exact
    /// match, so a fixture with only the first cannot fail.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        var stage = db.SeedCatalog();

        db.SeedHospital(Militaire, "Hôpital Militaire Mohammed V");
        var civil = db.SeedService(CivilService, "Cardiologie");
        var military = db.SeedService(MilitaryService, "Cardiologie (militaire)", hospitalId: Militaire);
        db.Allow(stage, civil, military);

        var slot = db.SeedSlot(stage, 1, 1, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));

        var placed = db.SeedGroup(MilitaryRoster, MilitaryRoster);
        var unarranged = db.SeedGroup(UnarrangedRoster, UnarrangedRoster);

        var cohort = db.SeedCohortFor(stage, placed, 11);
        db.SeedCohortFor(stage, unarranged, 12);

        db.SeedSlotAssignment(1, cohort, slot, military);
    });

    private static string Placements(string query) => $"/api/groups/placements?{query}";

    private static async Task<RosterPlacementsResponse> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RosterPlacementsResponse>(Json))!;
    }

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    /// <summary>
    /// ⚠ <b>The binding test the feature turns on.</b> <c>Exclusively</c> has to reach the handler as
    /// itself. Lost in binding it becomes <c>Anywhere</c> — value 0 — and the unarranged roster is
    /// then returned as satisfying « tout au militaire », which is the exact failure the enum exists
    /// to prevent and the one nothing on the wire would reveal.
    /// </summary>
    [Fact]
    public async Task The_match_mode_survives_the_query_string()
    {
        using var client = _factory.CreateApiClient();

        var body = await ReadAsync(await client.GetAsync(
            Placements($"levelId={TestHarness.LevelId}&hospitalId={Militaire}&match=Exclusively")));

        body.Rosters.Items.Select(r => r.GroupId).Should().Equal(MilitaryRoster);
        body.Rosters.Items.Single().HospitalPlacement.Should().Be(
            RosterHospitalPlacement.Entire);
    }

    /// <summary>
    /// The control. Without it a route answering an empty page to everything — a typo in the path, a
    /// binding failure — would satisfy the assertion above and prove nothing.
    /// </summary>
    [Fact]
    public async Task Without_a_target_the_whole_promotion_comes_back()
    {
        using var client = _factory.CreateApiClient();

        var body = await ReadAsync(await client.GetAsync(
            Placements($"levelId={TestHarness.LevelId}")));

        body.Rosters.Items.Select(r => r.GroupId).Should().Equal(MilitaryRoster, UnarrangedRoster);
        body.Summary.PromotionRosters.Should().Be(2);
        body.Summary.PlacedRosters.Should().Be(1,
            "an empty answer elsewhere has to be distinguishable from a promotion nobody arranged");
    }

    /// <summary>
    /// A service belongs to exactly one hospital, so naming both is either redundant or
    /// contradictory — and contradictory it answers an empty page that reads as « personne n'y va ».
    /// Refused rather than answered.
    /// </summary>
    [Fact]
    public async Task A_service_and_a_hospital_together_are_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(Placements(
            $"levelId={TestHarness.LevelId}&serviceId={MilitaryService}&hospitalId={Militaire}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// « Exclusivement » with nothing to be exclusive to would fall back to listing the promotion,
    /// i.e. answer a much weaker question than the one asked — silently.
    /// </summary>
    [Fact]
    public async Task Exclusively_without_a_target_is_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(Placements(
            $"levelId={TestHarness.LevelId}&match=Exclusively"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The promotion is the boundary of the whole read, so an omitted <c>levelId</c> binds to 0 and
    /// has to be refused — not answered for whichever promotion happens to carry id 0.
    /// </summary>
    [Fact]
    public async Task Omitting_the_promotion_is_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(Placements("hospitalId=2"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Sending no header leaves the request anonymous — a read that always authenticated could not
    /// tell « autorisé » from « jamais vérifié ».
    /// </summary>
    [Fact]
    public async Task The_read_requires_authentication()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync(Placements($"levelId={TestHarness.LevelId}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The feasibility read, and the one row it exists for: a stage whose authorised services are all
    /// elsewhere. Its route takes <c>levelId</c> as a required query parameter, which only the
    /// pipeline enforces.
    /// </summary>
    [Fact]
    public async Task Stage_coverage_answers_for_a_promotion_and_needs_one()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/hospitals/{Militaire}/stage-coverage?levelId={TestHarness.LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await response.Content.ReadFromJsonAsync<HospitalStageCoverageResponse>(Json))!;
        body.StageCount.Should().Be(1);
        body.CoveredStageCount.Should().Be(1);
        body.UnauthoredStageCount.Should().Be(0);
        body.Stages.Single().ServicesAtHospital.Single().ServiceId.Should().Be(MilitaryService);

        var unknown = await client.GetAsync($"/api/hospitals/4242/stage-coverage?levelId={TestHarness.LevelId}");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TitleAsync(unknown)).Should().Be("Hospitals.NotFound");

        var noLevel = await client.GetAsync($"/api/hospitals/{Militaire}/stage-coverage");
        noLevel.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
