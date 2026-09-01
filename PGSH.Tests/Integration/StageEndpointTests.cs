using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Stages;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>PUT stages/{id}</c> through the real pipeline.
///
/// <para>⚠ <b>This is the file that had to exist.</b> <c>StageRotationModePersistenceTests</c> calls
/// <c>UpdateStageCommandHandler</c> directly and was green throughout, because the validator does not
/// live in the handler — it runs in <c>ValidationPipelineBehavior</c>, which only a request through
/// the pipeline reaches. <c>UpdateStageCommandValidator</c> required at least one objective; the
/// Access import carried none, so <b>0 of the 27 stages in the live base</b> satisfied it and the
/// whole catalogue was read-only. Switching a stage to « un seul service pour tout le stage » came
/// back refused, naming a field the form was not editing.</para>
///
/// <para>Objectives are optional in the domain — only <c>EvaluationMode.ValidateObjectives</c> needs
/// any, and that is checked where it is true. So the rule described the stage somebody would author
/// and was applied to every save. Same shape as the CNE regex that made 5,646 students unsaveable.</para>
/// </summary>
public class StageEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;
    private const int StageId = 1;

    private readonly ApiFactory _factory;

    public StageEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A stage exactly as the legacy import left it: no objectives at all.</summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Chirurgie", LevelId = LevelId,
            Coefficient = 2, DurationInDays = 44,
            RotationMode = StageRotationMode.PerPeriod,
        });
    });

    private static string Url => $"/api/stages/{StageId}";

    private static object Body(string mode, object[] objectives) => new
    {
        name = "Chirurgie",
        coefficient = 2,
        description = (string?)null,
        durationInDays = 44,
        levelId = LevelId,
        objectives,
        rotationMode = mode,
    };

    private Task<StageRotationMode> ModeAsync() =>
        _factory.QueryAsync(db => db.Stages.Where(s => s.Id == StageId)
            .Select(s => s.RotationMode).SingleAsync());

    private static async Task<(string? Title, string? Detail)> ProblemAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null,
            doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null);
    }

    /// <summary>The reported bug, at the boundary that actually saw it.</summary>
    [Fact]
    public async Task A_stage_with_no_objectives_can_be_switched_to_single_service()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(Url, Body("SingleService", []));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "every imported stage carries zero objectives, so requiring one makes the catalogue read-only");
        (await ModeAsync()).Should().Be(StageRotationMode.SingleService);
    }

    [Fact]
    public async Task And_back_to_per_period()
    {
        using var client = _factory.CreateApiClient();

        await client.PutAsJsonAsync(Url, Body("SingleService", []));
        var response = await client.PutAsJsonAsync(Url, Body("PerPeriod", []));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ModeAsync()).Should().Be(StageRotationMode.PerPeriod);
    }

    /// <summary>
    /// The control, and the reason the rule was only relaxed rather than removed: an objective that
    /// <em>is</em> supplied is still validated, so a route that accepted everything would fail here.
    /// </summary>
    [Fact]
    public async Task An_objective_without_a_label_is_still_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(Url, Body("SingleService",
            [new { label = "", description = (string?)null, weight = 1, isMandatory = true }]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ModeAsync()).Should().Be(StageRotationMode.PerPeriod,
            "a refused save must not have written the mode on its way to failing");
    }

    [Fact]
    public async Task An_objective_with_a_zero_weight_is_still_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(Url, Body("SingleService",
            [new { label = "Savoir suturer", description = (string?)null, weight = 0, isMandatory = true }]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ModeAsync()).Should().Be(StageRotationMode.PerPeriod);
    }

    /// <summary>A well-formed objective still saves, so the relaxation did not break authoring.</summary>
    [Fact]
    public async Task A_valid_objective_is_still_accepted()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(Url, Body("SingleService",
            [new { label = "Savoir suturer", description = (string?)null, weight = 3, isMandatory = true }]));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ModeAsync()).Should().Be(StageRotationMode.SingleService);

        var objectives = await _factory.QueryAsync(db =>
            db.StageObjectives.Where(o => o.StageId == StageId).CountAsync());
        objectives.Should().Be(1);
    }

    /// <summary>
    /// The refusal has to be readable. The page showed « Erreur lors de l'enregistrement » for every
    /// failure, so the one message that explained the problem never reached anyone.
    /// </summary>
    [Fact]
    public async Task A_refusal_carries_a_message()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(Url, Body("SingleService",
            [new { label = "", description = (string?)null, weight = 1, isMandatory = true }]));

        var (title, _) = await ProblemAsync(response);
        title.Should().Be("Validation.General");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue(
            "the frontend reads the individual messages out of errors[]; detail is only the generic line");
        errors.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_edit_a_stage()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PutAsJsonAsync(Url, Body("SingleService", []));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
