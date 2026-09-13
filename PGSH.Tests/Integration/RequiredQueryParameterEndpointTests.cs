using System.Net;
using System.Text.Json;
using FluentAssertions;
using PGSH.Domain.Registrations;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// What happens when a screen sends a request with a field left empty.
///
/// <para>⚠ <b>Reported from the running application on 13/09/2026</b>, from the rotation-cycle screen:
/// <c>BadHttpRequestException: Required parameter "DateOnly StartDate" was not provided from query
/// string</c>, thrown inside <c>EndpointMiddleware</c>. A non-nullable value type bound from the query
/// string cannot be omitted — ASP.NET throws during <b>routing</b>, so
/// <c>ValidationPipelineBehavior</c> never runs, the query's own validator never runs, and the caller
/// gets a bare 400 carrying no <c>detail</c> and no <c>errors[]</c>. The client's
/// <c>errorMiddleware</c> then shows its generic sentence and the screen reads as broken rather than
/// as « renseignez une date ».</para>
///
/// <para>⚠ <b>A handler test cannot see any of this</b>, and neither can a validator test: the failure
/// is in model binding, which only a request through the real pipeline reaches. That is what this file
/// is for.</para>
///
/// <para>⚠ <b>The rule is not « make everything optional ».</b> It is that a parameter a <i>screen</i>
/// can leave blank is bound nullable and refused by a validator, in words. A POST act's
/// <c>confirmedCount</c> stays required: a caller that omits it is a broken client, not a user with an
/// empty field, and 400 is the whole of the right answer.</para>
/// </summary>
public class RequiredQueryParameterEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;
    private const int YearId = 1;

    private readonly ApiFactory _factory;

    public RequiredQueryParameterEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await _factory.SeedAsync(db =>
        {
            db.AcademicYears.Add(new AcademicYear
            {
                Id = YearId, Label = "2025-2026", IsCurrent = true,
                StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
            });
            db.Levels.Add(new Level
            {
                Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
                AcademicProgram = AcademicProgram.Medecine,
            });
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<(string? Title, string? Detail, string[] Errors)> ProblemAsync(
        HttpResponseMessage response)
    {
        string raw = await response.Content.ReadAsStringAsync();
        if (raw.Length == 0) return (null, null, []);

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        string[] errors = root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array
            ? [.. e.EnumerateArray().Select(x =>
                x.TryGetProperty("description", out var d) ? d.GetString() ?? "" : x.ToString())]
            : [];

        return (
            root.TryGetProperty("title", out var t) ? t.GetString() : null,
            root.TryGetProperty("detail", out var dt) ? dt.GetString() : null,
            errors);
    }

    // ─── The one that was actually reported ───────────────────────────────────

    [Fact]
    public async Task The_axis_without_a_start_date_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/stages/axis-windows?columns=10");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("date"),
            "a bare framework 400 carries no errors[] at all — that is the defect");
    }

    [Fact]
    public async Task The_axis_without_a_column_count_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/stages/axis-windows?startDate=2026-09-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("colonnes"));
    }

    /// <summary>
    /// The control. Without it every assertion above would also pass on a route that 400s on
    /// everything — a typo in the path, a binding failure — and prove nothing.
    /// </summary>
    [Fact]
    public async Task The_axis_with_both_still_answers()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/stages/axis-windows?columns=3&startDate=2025-10-01&levelId={LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>And the validator's own range rule still bites, now that it can actually run.</summary>
    [Fact]
    public async Task A_column_count_out_of_range_is_still_refused()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/stages/axis-windows?columns=0&startDate=2025-10-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // ⚠ And it says so in the same language as every other refusal. Written `x.Columns!.Value`,
        // the rule printed « 'Columns Value' must be between 1 and 60 » — FluentValidation builds the
        // field name from the expression, so reaching through the nullable leaks it into the message.
        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("colonnes") && !e.Contains("Columns"));
    }

    // ─── The two siblings of the same shape ───────────────────────────────────

    [Fact]
    public async Task The_partitioning_read_without_a_promotion_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/groups/partitioning");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("promotion"));
    }

    [Fact]
    public async Task The_partitioning_read_with_a_promotion_still_answers()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/groups/partitioning?levelId={LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_placements_read_without_a_promotion_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/groups/placements");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("promotion"));
    }

    [Fact]
    public async Task The_placements_read_with_a_promotion_still_answers()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/groups/placements?levelId={LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── The two the first sweep missed ───────────────────────────────────────
    //
    // ⚠ The first pass scanned PGSH.Application for the declarations of every [AsParameters] type and
    // reported two it could not find — because they are declared *inside* the endpoint classes. I read
    // that as noise instead of as the answer, and the second one took the running stack down again the
    // same afternoon. Both are here now, and so is the parameter type the sweep also under-counted: an
    // **enum** is a value type, so an omitted one throws exactly like a DateOnly.

    [Fact]
    public async Task The_service_occupants_read_without_a_window_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/services/1/occupants");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (title, _, _) = await ProblemAsync(response);
        title.Should().Be("ServiceOccupancy.WindowRequired");
    }

    [Fact]
    public async Task The_service_occupants_read_with_a_window_does_not_refuse_on_the_window()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            "/api/services/1/occupants?startDate=2025-10-01&endDate=2025-10-31");

        // The service does not exist in this fixture, so 404 or 200 are both fine — what must not
        // happen is a 400 about the window, which is what the control is for.
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    /// <summary>An enum is a value type: an omitted <c>scope</c> threw in routing like any other.</summary>
    [Fact]
    public async Task The_evaluation_import_without_a_scope_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("not a workbook"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, "file", "notes.xlsx");

        var response = await client.PostAsync(
            $"/api/stages/{StageId}/evaluations/import/preview?mode=Numeric", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an omitted enum used to throw inside routing instead of being refused");
    }

    private const int StageId = 1;
}
