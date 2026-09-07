using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Calendar;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// The promotion-pause routes through the real pipeline.
///
/// <para>⚠ <b>The half a handler test cannot see.</b> Three of the rules here live outside the handler:
/// the validator runs in <c>ValidationPipelineBehavior</c>, the mapping from <c>Result.Failure</c> to a
/// problem response is wired in <c>Program.cs</c>, and authentication is middleware. A window that
/// declares a promotion's whole calendar out of service is exactly the act whose refusals have to be
/// refusals at the boundary and not somewhere inside.</para>
///
/// <para>Every refusal is paired with the request that must still succeed. A route that 400s on
/// everything — a typo in the path, a binding failure — satisfies every refusal assertion and proves
/// nothing.</para>
/// </summary>
public class PromotionPauseEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int LevelId = 3;
    private const int OtherLevelId = 4;
    private const int MarkerLevelId = 9;

    private static readonly DateOnly ExamStart = new(2026, 1, 12);
    private static readonly DateOnly ExamEnd = new(2026, 1, 16);

    private readonly ApiFactory _factory;

    public PromotionPauseEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = YearId,
            Label = "2025-2026",
            IsCurrent = true,
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2026, 8, 31),
        });

        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Levels.Add(new Level
        {
            Id = OtherLevelId, Label = "Quatrième Année Médecine", Year = 4,
            AcademicProgram = AcademicProgram.Medecine,
        });

        // « Retrait »: a withdrawal marker the import kept as a level. Year 0, so it sits no exams —
        // and once it stopped being offered in the UI this refusal became unreachable by hand.
        db.Levels.Add(new Level
        {
            Id = MarkerLevelId, Label = "Retrait", Year = 0,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Stages.Add(new Stage
        {
            Id = 1, Name = "Chirurgie", LevelId = LevelId, Coefficient = 2, DurationInDays = 44,
        });

        db.StageSlots.Add(new StageSlot
        {
            Id = 1, StageId = 1, AcademicYearId = YearId, PeriodNumber = 1,
            StartDate = new DateOnly(2026, 1, 5), EndDate = new DateOnly(2026, 2, 6),
        });
    });

    private const string Url = "/api/calendar/promotion-pauses";

    private static object Body(
        int levelId = LevelId, string reason = "Examens du 1er semestre",
        DateOnly? start = null, DateOnly? end = null) => new
        {
            levelId,
            startDate = (start ?? ExamStart).ToString("yyyy-MM-dd"),
            endDate = (end ?? ExamEnd).ToString("yyyy-MM-dd"),
            kind = "Exam",
            reason,
            isConfirmed = true,
        };

    private Task<int> CountAsync() => _factory.QueryAsync(db => db.PromotionPauses.CountAsync());

    private static async Task<(string? Title, string? Detail, string? FirstError)> ProblemAsync(
        HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        string? firstError = null;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            firstError = first.ValueKind == JsonValueKind.String
                ? first.GetString()
                : first.TryGetProperty("description", out var d) ? d.GetString() : null;
        }

        return (
            root.TryGetProperty("title", out var t) ? t.GetString() : null,
            root.TryGetProperty("detail", out var det) ? det.GetString() : null,
            firstError);
    }

    /// <summary>The control every refusal below is measured against.</summary>
    [Fact]
    public async Task A_promotion_can_declare_an_exam_window()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Url, Body());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_and_writes_nothing()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(Url, Body());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The validator lives in the pipeline, not in the handler — a test that called the handler
    /// directly would pass this command straight through.
    /// </summary>
    [Fact]
    public async Task A_window_with_no_reason_is_refused_at_the_boundary()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Url, Body(reason: ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync()).Should().Be(0, "a refused act must not have written on its way to failing");
    }

    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Url, Body(start: ExamEnd, end: ExamStart));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// A domain refusal, and the one that says the message reaches the screen: it names « Retrait »
    /// rather than a field the form was not editing.
    /// </summary>
    [Fact]
    public async Task The_withdrawal_marker_cannot_declare_a_window()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Url, Body(levelId: MarkerLevelId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync()).Should().Be(0);

        var (_, detail, firstError) = await ProblemAsync(response);
        (detail ?? firstError).Should().Contain("Retrait");
    }

    [Fact]
    public async Task A_window_outside_the_year_it_names_is_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(Url,
            Body(start: new DateOnly(2026, 8, 26), end: new DateOnly(2026, 9, 4)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_second_window_overlapping_the_first_is_refused_and_another_promotion_is_not()
    {
        using var client = _factory.CreateApiClient();

        (await client.PostAsJsonAsync(Url, Body())).StatusCode.Should().Be(HttpStatusCode.Created);

        var overlapping = await client.PostAsJsonAsync(Url,
            Body(reason: "Rattrapage", start: ExamEnd, end: ExamEnd.AddDays(2)));

        overlapping.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CountAsync()).Should().Be(1);

        // The control, and the reason the guard is keyed on the pair: two promotions sit different
        // exams on the same morning.
        var other = await client.PostAsJsonAsync(Url, Body(levelId: OtherLevelId));

        other.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task The_preview_writes_nothing_and_names_what_the_window_crosses()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync($"{Url}/preview", Body());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountAsync()).Should().Be(0, "a dry run is a dry run");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("workingDaysLost").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("slotsSpanning").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Correcting_a_window_that_is_not_there_is_a_not_found()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync($"{Url}/4242", new
        {
            startDate = ExamStart.ToString("yyyy-MM-dd"),
            endDate = ExamEnd.ToString("yyyy-MM-dd"),
            kind = "Exam",
            reason = "Examens",
            isConfirmed = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_declared_window_can_be_corrected_and_then_revoked()
    {
        using var client = _factory.CreateApiClient();

        var created = await client.PostAsJsonAsync(Url, Body());
        int id = await _factory.QueryAsync(db => db.PromotionPauses.Select(p => p.Id).SingleAsync());
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var corrected = await client.PutAsJsonAsync($"{Url}/{id}", new
        {
            startDate = ExamStart.AddDays(1).ToString("yyyy-MM-dd"),
            endDate = ExamEnd.AddDays(1).ToString("yyyy-MM-dd"),
            kind = "Exam",
            reason = "Examens du 1er semestre",
            isConfirmed = true,
        });

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.QueryAsync(db => db.PromotionPauses.Select(p => p.StartDate).SingleAsync()))
            .Should().Be(ExamStart.AddDays(1));

        var revoked = await client.DeleteAsync($"{Url}/{id}");

        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The register is the only trace a revocation leaves — the aggregate root goes, and EF detaches a
    /// deleted entity before domain events are collected, so an event raised there would vanish.
    /// </summary>
    [Fact]
    public async Task Declaring_and_revoking_are_both_recorded_in_the_register()
    {
        using var client = _factory.CreateApiClient();

        await client.PostAsJsonAsync(Url, Body());
        int id = await _factory.QueryAsync(db => db.PromotionPauses.Select(p => p.Id).SingleAsync());
        await client.DeleteAsync($"{Url}/{id}");

        var actions = await _factory.QueryAsync(db => db.AuditLogs
            .Select(a => a.Action)
            .ToListAsync());

        actions.Should().Contain("PROMOTION_PAUSE_DECLARED");
        actions.Should().Contain("PROMOTION_PAUSE_REVOKED");
    }

    /// <summary>
    /// The declared window reaches the axis the faculty then lays — the whole compensation mechanism,
    /// asked through the route the rotation-cycle screen actually calls.
    /// </summary>
    [Fact]
    public async Task The_axis_route_lays_its_columns_over_the_declared_window()
    {
        using var client = _factory.CreateApiClient();

        string axis = $"/api/stages/axis-windows?columns=1&startDate=2026-01-05"
            + $"&unit=WorkingDays&length=15&levelId={LevelId}";

        var before = await client.GetAsync(axis);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        var endBefore = await ColumnEndAsync(before);

        (await client.PostAsJsonAsync(Url, Body())).StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await client.GetAsync(axis);
        (await ColumnEndAsync(after)).Should().Be(endBefore.AddDays(7),
            "the exam week is stepped over rather than served");

        // The control: the promotion next door did not declare anything and its axis does not move.
        var neighbour = await client.GetAsync(axis.Replace($"levelId={LevelId}", $"levelId={OtherLevelId}"));
        (await ColumnEndAsync(neighbour)).Should().Be(endBefore);
    }

    private static async Task<DateOnly> ColumnEndAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return DateOnly.Parse(
            doc.RootElement.GetProperty("columns")[0].GetProperty("endDate").GetString()!);
    }
}
