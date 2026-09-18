using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
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
    private const int NextYearId = 2;

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
            db.AcademicYears.Add(new AcademicYear
            {
                Id = NextYearId, Label = "2026-2027", IsCurrent = false,
                StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31),
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

    // ─── The four roster acts (HANDOFF 0bs) ───────────────────────────────────
    //
    // ⚠ These are the first *write* acts taken off the KnownOffenders list, and they are refused
    // rather than resolved. An omitted academic year means « the current one » on a read — that is
    // AcademicYearResolver's rule and it is right there. It is the wrong rule for an act that
    // destroys: a year nobody named is a year nobody consented to, and the current one is precisely
    // the promotion everybody is working on.

    [Fact]
    public async Task Cutting_a_promotion_without_a_year_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            $"/api/groups/assign-partitions?levelId={LevelId}", new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("année universitaire"),
            "a bare framework 400 carries no errors[] at all — that is the defect");
    }

    [Fact]
    public async Task Cutting_a_promotion_without_a_promotion_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            $"/api/groups/assign-partitions?academicYearId={YearId}", new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("Non réparti"),
            "the sentence has to say what the omission would have reached, not merely that a field is missing");
    }

    /// <summary>
    /// The control. Without it both assertions above would also pass on a route that 400s on
    /// everything, and prove nothing.
    /// </summary>
    [Fact]
    public async Task Cutting_a_promotion_with_both_still_answers()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            $"/api/groups/assign-partitions?academicYearId={YearId}&levelId={LevelId}",
            new { partitionCount = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Undoing_a_cut_without_a_year_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync($"/api/groups/partitions?levelId={LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("année universitaire"));
    }

    [Fact]
    public async Task Undoing_a_cut_without_a_promotion_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync($"/api/groups/partitions?academicYearId={YearId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("promotion"));
    }

    [Fact]
    public async Task Undoing_a_cut_with_both_still_answers()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync(
            $"/api/groups/partitions?academicYearId={YearId}&levelId={LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_every_roster_without_a_year_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync("/api/groups/all");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("année universitaire"));
    }

    [Fact]
    public async Task Emptying_every_roster_without_a_year_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync("/api/groups/all/students");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("année universitaire"));
    }

    /// <summary>
    /// The control for the two teardown acts, and it is deliberately weaker than the others.
    ///
    /// <para>⚠ Both write only through <c>ExecuteDelete</c>/<c>ExecuteUpdate</c>, which
    /// <c>UseInMemoryDatabase</c> <b>refuses outright</b> — so their success path is not reachable from
    /// this suite by any route and <c>Should().Be(OK)</c> would be asserting the provider, not the
    /// act. What the control can still prove is the thing it exists to prove: that a request carrying
    /// the year is not refused <i>for the year</i>. It becomes a plain 200 the day item 9
    /// (Testcontainers) lands.</para>
    /// </summary>
    [Theory]
    [InlineData("/api/groups/all")]
    [InlineData("/api/groups/all/students")]
    public async Task A_teardown_carrying_its_year_is_not_refused_for_the_year(string route)
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync($"{route}?academicYearId={YearId}");

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().NotContain(e => e.Contains("année universitaire"));
    }

    // ─── The inscription canvas ───────────────────────────────────────────────
    //
    // One sentence for the three inscription routes, and that is honest rather than lazy: the reason
    // is a fact about the *sheet* — the people it names hold no registration yet, so nothing in the
    // data carries a promotion. → InscriptionErrors.PromotionRequiredMessage.

    [Fact]
    public async Task The_inscription_canvas_without_a_promotion_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/inscription/template");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("pas encore inscrits"),
            "the sentence has to say why the promotion cannot be deduced, not only that it is missing");
    }

    /// <summary>
    /// ⚠ The control needs the role the canvas is gated on. The <i>refusal</i> above deliberately does
    /// not: validation runs before the handler, so an anonymous caller with an empty selector is told
    /// what is missing rather than that he may not ask — and that ordering is worth pinning.
    /// </summary>
    [Fact]
    public async Task The_inscription_canvas_with_a_promotion_still_answers()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.GetAsync($"/api/inscription/template?levelId={LevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── The rollover's two years ─────────────────────────────────────────────
    //
    // ⚠ Two sentences, not one: the year being closed and the year being opened are different facts,
    // and a single « année obligatoire » would leave the operator guessing which selector to fill.

    [Fact]
    public async Task The_rollover_without_its_departure_year_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/reinscription/preview?toAcademicYearId={NextYearId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("année de départ"));
    }

    [Fact]
    public async Task The_rollover_without_its_destination_year_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/reinscription/preview?fromAcademicYearId={YearId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("année de destination"));
    }

    /// <summary>
    /// The control, and it carries two <i>distinct</i> years on purpose: a rollover onto the same year
    /// is refused by the planner, so a lazier control would have passed for the wrong reason.
    /// </summary>
    [Fact]
    public async Task The_rollover_with_both_years_is_not_refused_for_a_year()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/reinscription/preview?fromAcademicYearId={YearId}&toAcademicYearId={NextYearId}");

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().NotContain(e => e.Contains("est obligatoire"));
    }

    // ─── The last four, each a different kind of missing thing ────────────────

    [Fact]
    public async Task Hospital_coverage_without_a_promotion_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/hospitals/1/stage-coverage");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("promotion"));
    }

    /// <summary>
    /// The hospital is not in this fixture, so 404 is the right answer. What must not happen is a 400
    /// about the promotion — that is the whole of what the control proves.
    /// </summary>
    [Fact]
    public async Task Hospital_coverage_with_a_promotion_is_not_refused_for_the_promotion()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/hospitals/1/stage-coverage?levelId={LevelId}");

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("toCnpnVersionId=2", "texte de départ")]
    [InlineData("fromCnpnVersionId=1", "texte d'arrivée")]
    public async Task A_curriculum_comparison_missing_one_text_names_which_one(
        string given, string expected)
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/levels/{LevelId}/curriculum/compare?{given}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains(expected),
            "a comparison missing one of its two texts has to say which of the two");
    }

    [Fact]
    public async Task A_curriculum_comparison_with_both_texts_is_not_refused_for_a_text()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/levels/{LevelId}/curriculum/compare?fromCnpnVersionId=1&toCnpnVersionId=2");

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_revalidation_dialog_without_a_stage_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/registrations/{Guid.NewGuid()}/revalidation-context");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("stage est obligatoire"));
    }

    /// <summary>
    /// ⚠ Not a screen selector — a caller omitting the owner is a broken client, not a user with an
    /// empty field. It is fixed the same way regardless: the parameter stays required, and what
    /// changes is that the refusal can be read instead of being thrown by the model binder.
    /// </summary>
    [Fact]
    public async Task Deleting_a_registration_without_its_owner_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync($"/api/registrations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (_, _, errors) = await ProblemAsync(response);
        errors.Should().Contain(e => e.Contains("à qui elle appartient"));
    }

    /// <summary>
    /// The canvas route bound its two enums bare while the two upload routes beside it already went
    /// through <c>ImportOptions</c> — and it is the one of the three a user reaches <i>first</i>.
    /// </summary>
    [Fact]
    public async Task The_evaluation_canvas_without_a_scope_is_refused_by_name()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/stages/{StageId}/evaluations/import/template?mode=Numeric");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (title, _, _) = await ProblemAsync(response);
        title.Should().Be("EvaluationImport.ScopeRequired");
    }

    [Fact]
    public async Task The_evaluation_canvas_without_a_mode_is_refused_by_name()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/stages/{StageId}/evaluations/import/template?scope=WholeStage");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (title, _, _) = await ProblemAsync(response);
        title.Should().Be("EvaluationImport.ModeRequired");
    }

    private const int StageId = 1;
}
