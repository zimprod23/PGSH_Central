using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// Phase 17.1 — moving a published column — through the real pipeline.
///
/// <para>⚠ <b>Why this file exists although eleven handler tests already cover the act.</b>
/// <c>PublishedColumnMoveTests</c> constructs the command directly, so it never reaches routing, model
/// binding, <c>ValidationPipelineBehavior</c> or the <c>Result.Failure</c> → problem mapping. Those are
/// exactly the layers that turn a carefully written refusal into a bare 400 the screen can only render
/// as « Données invalides ». And this act is the remedy the pause report now <em>prescribes</em> — it
/// tells an operator to move the crossed columns one at a time — so the boundary it is reached through
/// has to be the tested one.</para>
///
/// <para>⚠ <b>And the last case is the one nothing else in the repository asserts</b>: that after the
/// move the <c>ServicePeriod</c> carries the new dates. That is the whole content of the phase — before
/// it, the grid and the student's dossier disagreed in silence — and until now the only way to see it
/// was to move a column on the live base.</para>
///
/// <para>Every refusal is paired with the request that must still succeed, and every refusal asserts
/// that the store did not move: a guard ordered <em>after</em> the write returns the same failure and
/// passes a handler test.</para>
/// </summary>
public class PublishedColumnMoveEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int SlotP1 = 6101, SlotP2 = 6102;
    private const int CellP1 = 6201, CellP2 = 6202;
    private const int ServiceId = 30;

    private static readonly DateOnly P1Start = new(2026, 3, 2),  P1End = new(2026, 3, 13);
    private static readonly DateOnly P2Start = new(2026, 3, 16), P2End = new(2026, 3, 27);

    // Where P2 is moved to: later, so it cannot collide with P1. Nothing follows it, which is the only
    // shape a single move can take — the act deliberately does not cascade.
    private static readonly DateOnly MovedStart = new(2026, 3, 23), MovedEnd = new(2026, 4, 3);

    private readonly ApiFactory _factory;

    public PublishedColumnMoveEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Two columns of one stage, a cohorte standing in a service across both, and a published période
    /// per column — the ordinary <c>PerPeriod</c> shape, so that the window that moves is P2's own.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        var stage = db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Cardiologie A");
        var cohort = db.SeedCohort(stage, groupId: 1, groupLabel: "G1");

        var p1 = db.SeedSlot(stage, SlotP1, 1, P1Start, P1End);
        var p2 = db.SeedSlot(stage, SlotP2, 2, P2Start, P2End);

        var c1 = db.SeedSlotAssignment(CellP1, cohort, p1, service);
        var c2 = db.SeedSlotAssignment(CellP2, cohort, p2, service);

        var registration = db.SeedRegistration("Amina", "Benali");
        var assignment = db.SeedAssignment(registration, cohort);

        var first = db.SeedPeriod(assignment, service, P1Start, P1End, started: false);
        db.SeedCoverage(first, c1);

        var second = db.SeedPeriod(assignment, service, P2Start, P2End, started: false);
        db.SeedCoverage(second, c2);
    });

    private static string PreviewUrl(DateOnly? start, DateOnly? end)
    {
        var query = new List<string>();
        if (start is { } s) query.Add($"startDate={s:yyyy-MM-dd}");
        if (end is { } e) query.Add($"endDate={e:yyyy-MM-dd}");

        return $"/api/stages/{TestHarness.StageId}/slots/{SlotP2}/move-preview"
             + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
    }

    private static string MoveUrl => $"/api/stages/{TestHarness.StageId}/slots/{SlotP2}";

    private static object MoveBody(DateOnly start, DateOnly end, int? confirmed) => new
    {
        label = (string?)null,
        startDate = start.ToString("yyyy-MM-dd"),
        endDate = end.ToString("yyyy-MM-dd"),
        confirmedPeriodCount = confirmed,
    };

    /// <summary>The column's stored window — what every refusal below must leave alone.</summary>
    private Task<(DateOnly Start, DateOnly End)> SlotWindowAsync() =>
        _factory.QueryAsync(async db =>
        {
            var slot = await db.StageSlots.AsNoTracking().FirstAsync(s => s.Id == SlotP2);
            return (slot.StartDate, slot.EndDate);
        });

    /// <summary>The période published from that column — the half a grid-only assertion cannot see.</summary>
    private Task<(DateOnly Start, DateOnly End)> PeriodWindowAsync() =>
        _factory.QueryAsync(async db =>
        {
            var period = await db.ServicePeriods
                .AsNoTracking()
                .OrderBy(p => p.StartDate)
                .LastAsync();

            return (period.StartDate, period.EndDate);
        });

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

    /// <summary>The control every refusal is measured against.</summary>
    [Fact]
    public async Task The_preview_answers_and_reports_what_the_move_would_cover()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(PreviewUrl(MovedStart, MovedEnd));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("periodsCovered").GetInt32().Should().BeGreaterThan(0);
        root.GetProperty("refusalMessage").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("currentStartDate").GetString().Should().Be(P2Start.ToString("yyyy-MM-dd"));
        root.GetProperty("proposedStartDate").GetString().Should().Be(MovedStart.ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// ⚠ <b>The case only the pipeline can see.</b> A non-nullable <c>DateOnly</c> bound from the query
    /// string throws in <c>EndpointMiddleware</c> <em>before</em> the validator runs, so the caller gets
    /// a bare 400 with no sentence and the screen reads as broken rather than as « renseignez une date ».
    /// Both dates are bound nullable precisely so this refusal is a sentence.
    /// </summary>
    [Fact]
    public async Task The_preview_without_a_start_date_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(PreviewUrl(null, MovedEnd));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, firstError) = await ProblemAsync(response);
        firstError.Should().NotBeNullOrWhiteSpace();
        firstError.Should().Contain("date de début");
    }

    /// <summary>
    /// ⚠ Confirming nothing and confirming the wrong number are <b>two</b> refusals, because the acts
    /// they call for differ: fetch the preview, versus fetch it again because the plan moved under you.
    /// </summary>
    [Fact]
    public async Task Moving_a_published_column_without_confirming_is_refused_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(MoveUrl, MoveBody(MovedStart, MovedEnd, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var (title, detail, _) = await ProblemAsync(response);
        $"{title} {detail}".Should().Contain("Schedule.SlotMoveNotConfirmed");

        (await SlotWindowAsync()).Should().Be((P2Start, P2End));
        (await PeriodWindowAsync()).Should().Be((P2Start, P2End));
    }

    [Fact]
    public async Task Confirming_the_wrong_number_is_refused_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync(MoveUrl, MoveBody(MovedStart, MovedEnd, 99));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var (title, detail, _) = await ProblemAsync(response);
        $"{title} {detail}".Should().Contain("Schedule.SlotMoveCountMismatch");

        (await SlotWindowAsync()).Should().Be((P2Start, P2End));
        (await PeriodWindowAsync()).Should().Be((P2Start, P2End));
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_and_writes_nothing()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PutAsJsonAsync(MoveUrl, MoveBody(MovedStart, MovedEnd, 1));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SlotWindowAsync()).Should().Be((P2Start, P2End));
    }

    /// <summary>
    /// ⚠ <b>The assertion the whole phase exists for, and nothing else in this repository makes it at
    /// the boundary</b>: the column moves <em>and the période published from it moves with it</em>.
    /// Before 17.1 the two halves could be rewritten independently, so the planning grid and the
    /// student's dossier disagreed with nothing on either side saying so.
    /// </summary>
    [Fact]
    public async Task A_confirmed_move_shifts_the_column_and_the_periode_published_from_it()
    {
        using var client = _factory.CreateApiClient();

        var preview = await client.GetAsync(PreviewUrl(MovedStart, MovedEnd));
        using var previewDoc = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        int covered = previewDoc.RootElement.GetProperty("periodsCovered").GetInt32();

        var response = await client.PutAsJsonAsync(MoveUrl, MoveBody(MovedStart, MovedEnd, covered));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("periodsCovered").GetInt32().Should().Be(covered);
        doc.RootElement.GetProperty("periodsShifted").GetInt32().Should().BeGreaterThan(0);

        (await SlotWindowAsync()).Should().Be((MovedStart, MovedEnd));
        (await PeriodWindowAsync()).Should().Be((MovedStart, MovedEnd),
            "the dossier and the grid are the two halves that must never drift apart");
    }

    /// <summary>
    /// ⚠ The move is not a re-publication: the cell stays attached to its column, so the grid still
    /// shows the same cohorte in the same service — only the dates moved.
    /// </summary>
    [Fact]
    public async Task The_move_leaves_the_cell_where_it_was()
    {
        using var client = _factory.CreateApiClient();

        var preview = await client.GetAsync(PreviewUrl(MovedStart, MovedEnd));
        using var previewDoc = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        int covered = previewDoc.RootElement.GetProperty("periodsCovered").GetInt32();

        await client.PutAsJsonAsync(MoveUrl, MoveBody(MovedStart, MovedEnd, covered));

        var cells = await _factory.QueryAsync(db => db.CohortSlotAssignments
            .AsNoTracking()
            .CountAsync(c => c.StageSlotId == SlotP2 && c.ServiceId == ServiceId));

        cells.Should().Be(1);
    }
}
