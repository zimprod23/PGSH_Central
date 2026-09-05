using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>PUT stages/{id}/allowed-services/order</c> through the real pipeline.
///
/// <para>What only this suite can see: that the refusal happens <b>before</b> the write. A guard
/// ordered after it returns the same <c>Result.Failure</c> and passes a handler test, so a refusal
/// case asserts the status code <em>and</em> that the ranks on disk are untouched.</para>
///
/// <para>⚠ Each refusal is paired with the request that must still succeed. A route that 400s on
/// everything — a typo in the path, a binding failure — satisfies every refusal assertion and proves
/// nothing.</para>
/// </summary>
public class StageServiceOrderEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;
    private const int StageId = 1;
    private const int HospitalId = 1;
    private const int First = 10;
    private const int Second = 11;
    private const int Third = 12;

    private readonly ApiFactory _factory;

    public StageServiceOrderEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        var stage = new Stage
        {
            Id = StageId, Name = "Chirurgie", LevelId = LevelId,
            Coefficient = 2, DurationInDays = 44,
        };
        db.Stages.Add(stage);

        var hospital = new Hospital { Id = HospitalId, Name = "CHU Ibn Sina", City = "Rabat" };
        db.Hospitals.Add(hospital);

        int rank = 1;
        foreach (var (id, name) in new[]
                 {
                     (First, "Cardiologie"), (Second, "Pneumologie"), (Third, "Rhumatologie"),
                 })
        {
            db.Services.Add(new Service
            {
                Id = id, Name = name, Description = "",
                HospitalId = hospital.Id, Hospital = hospital, Capacity = 20,
            });

            db.StageAllowedServices.Add(new StageAllowedService
            {
                StageId = StageId, ServiceId = id, Rank = rank++,
            });
        }
    });

    private static string Url => $"/api/stages/{StageId}/allowed-services/order";

    private Task<Dictionary<int, int>> RanksAsync() =>
        _factory.QueryAsync(db => db.StageAllowedServices
            .Where(a => a.StageId == StageId)
            .ToDictionaryAsync(a => a.ServiceId, a => a.Rank));

    [Fact]
    public async Task Authoring_an_order_is_accepted_and_written()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(Url, new { serviceIds = new[] { Third, First, Second } });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RanksAsync()).Should().BeEquivalentTo(
            new Dictionary<int, int> { [Third] = 1, [First] = 2, [Second] = 3 });
    }

    [Fact]
    public async Task An_order_that_leaves_a_service_out_is_refused_and_nothing_is_written()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(Url, new { serviceIds = new[] { Third, First } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RanksAsync()).Should().BeEquivalentTo(
            new Dictionary<int, int> { [First] = 1, [Second] = 2, [Third] = 3 },
            "the guard runs before the write");
    }

    [Fact]
    public async Task An_order_naming_a_service_the_stage_does_not_allow_is_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(
            Url, new { serviceIds = new[] { First, Second, Third, 999 } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RanksAsync()).Should().BeEquivalentTo(
            new Dictionary<int, int> { [First] = 1, [Second] = 2, [Third] = 3 });
    }

    [Fact]
    public async Task An_order_on_an_unknown_stage_is_a_404()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(
            "/api/stages/4242/allowed-services/order", new { serviceIds = Array.Empty<int>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Sending no header leaves the request anonymous — without this, a route that never checks
    /// cannot be told from one that always allows.
    /// </summary>
    [Fact]
    public async Task The_route_requires_authentication()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PutAsJsonAsync(Url, new { serviceIds = new[] { Third, First, Second } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RanksAsync()).Should().BeEquivalentTo(
            new Dictionary<int, int> { [First] = 1, [Second] = 2, [Third] = 3 });
    }
}
