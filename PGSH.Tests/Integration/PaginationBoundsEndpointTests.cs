using System.Net;
using System.Text.Json;
using FluentAssertions;
using PGSH.Application.Extensions;
using PGSH.Domain.Stages;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// The page-size ceiling, at the boundary that enforces it.
///
/// <para>⚠ <b>The rule was written twice and the two copies disagreed.</b>
/// <c>QueryableExtensions.MaxPageSize</c> is 200 and <b>clamps</b> — a larger page is served short,
/// never refused, and the response still carries the true <c>TotalCount</c>. Three query validators
/// spelled their own stricter ceiling of 100 and <em>refused</em>, so a request the pipeline would
/// have served happily never reached it.</para>
///
/// <para>What it cost: the CNPN editor asks <c>GET /stages?levelId=…&amp;pageSize=200</c> for the
/// stages a text may require of a level. That 400'd on every open, the stage list came back empty,
/// and the picker rendered disabled with « Tous les stages du niveau sont listés » — so two stages
/// the faculty had just created could not be required by any text, and the refusal read on screen
/// as a broken control rather than as a rule.</para>
///
/// <para>⚠ Invisible to a handler test: the validator lives in <c>ValidationPipelineBehavior</c>,
/// which only a request through the real pipeline reaches. Same blind spot that let
/// <c>UpdateStageCommandValidator</c> make the whole catalogue read-only — see
/// <see cref="StageEndpointTests"/>.</para>
/// </summary>
public class PaginationBoundsEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;

    private readonly ApiFactory _factory;

    public PaginationBoundsEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>MED3 as the faculty left it on 07/09/2026 — six stages, plus the two just added.</summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        string[] names =
        [
            "Cardiologie", "Chirurgie", "Dermatologie - Endocrinologie", "Médecine",
            "Pneumologie", "Rhumatologie - Radiologie", "Santé Publique", "Simulation Médicale",
        ];

        for (int i = 0; i < names.Length; i++)
        {
            db.Stages.Add(new Stage
            {
                Id = i + 1, Name = names[i], LevelId = LevelId,
                Coefficient = 1, DurationInDays = 15,
                RotationMode = StageRotationMode.SingleService,
            });
        }
    });

    private static async Task<int> ItemCountAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("items").GetArrayLength();
    }

    /// <summary>The reported bug: the CNPN editor's own read.</summary>
    [Fact]
    public async Task The_curriculum_editor_can_ask_for_a_whole_level_catalogue_in_one_page()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/stages?levelId={LevelId}&pageSize={QueryableExtensions.MaxPageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the pipeline clamps to this number, so refusing it rejects a page it would have served");
        (await ItemCountAsync(response)).Should().Be(8,
            "an empty list is what disabled the picker and hid the two new stages");
    }

    /// <summary>
    /// The control. A route that 400s on everything satisfies every refusal assertion and proves
    /// nothing; a route that accepts everything makes the ceiling meaningless. Above the ceiling the
    /// request is still refused, and the message names the real bound.
    /// </summary>
    [Fact]
    public async Task A_page_above_the_ceiling_is_still_refused_and_says_the_real_number()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/stages?levelId={LevelId}&pageSize={QueryableExtensions.MaxPageSize + 1}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errors")[0].GetProperty("description").GetString()
            .Should().Contain(QueryableExtensions.MaxPageSize.ToString(),
                "a refusal that names a bound the code no longer uses sends the reader to the wrong place");
    }

    [Fact]
    public async Task A_page_of_zero_is_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/stages?levelId={LevelId}&pageSize=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_page_number_below_one_is_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/stages?levelId={LevelId}&pageNumber=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The same ceiling on the other two lists the client asks a full page of. They had the same
    /// hand-written 100; sweeping only the one that was reported would leave the next one to find.
    /// </summary>
    [Theory]
    [InlineData("/api/levels")]
    [InlineData("/api/students")]
    public async Task Every_list_shares_one_ceiling(string route)
    {
        using var client = _factory.CreateApiClient();

        var accepted = await client.GetAsync($"{route}?pageSize={QueryableExtensions.MaxPageSize}");
        var refused = await client.GetAsync($"{route}?pageSize={QueryableExtensions.MaxPageSize + 1}");

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
