using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// The canevas des affectations through the real pipeline: the file is parsed here, the route's
/// required parameters are bound here, and the validator runs here and nowhere a handler test can
/// reach.
///
/// <para>⚠ <b>This is also the only place the round trip exists.</b> The download and the upload are
/// two halves of one document, and nothing in a handler test connects them: a canvas whose headers the
/// parser cannot find would produce « stage inconnu » on every line, refuse the whole file, and look
/// exactly like a user who typed the stage names wrong. So the canvas this suite uploads is the one
/// the endpoint hands out.</para>
/// </summary>
public class AffectationSheetEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;
    private const int StageId = 1;
    private const int ServiceId = 1;
    private const int GroupId = 10;
    private const int YearId = 1;
    private const string Appogee = "22000001";

    private static readonly DateOnly Start = new(2025, 10, 1);
    private static readonly DateOnly End = new(2025, 10, 31);

    private readonly ApiFactory _factory;

    public AffectationSheetEndpointTests(ApiFactory factory) => _factory = factory;

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
            Id = YearId, Label = "2025-2026", IsCurrent = true,
            StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
        });

        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Chirurgie", LevelId = LevelId, Coefficient = 2, DurationInDays = 30,
        });

        db.Centers.Add(new Center { Id = 1, Name = "CHU Ibn Sina", City = "Rabat" });
        db.Hospitals.Add(new Hospital { Id = 1, Name = "CHU Ibn Sina", City = "Rabat", CenterId = 1 });
        db.Services.Add(new Service
        {
            Id = ServiceId, Name = "Chirurgie A", Description = "", HospitalId = 1,
        });

        db.AcademicGroups.Add(new AcademicGroup
        {
            Id = GroupId, Label = "G10", GroupNumber = 10, AcademicYearId = YearId, LevelId = LevelId,
        });

        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = "Mohamed", LastName = "Alami",
            Email = "mohamed.alami@etu.ma", Appogee = Appogee, BacYear = "2022",
            AcademicProgram = AcademicProgram.Medecine,
        };
        db.Users.Add(student);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = LevelId,
            StudentId = student.Id, AcademicGroupId = GroupId,
        });
    });

    private HttpClient Admin() => _factory.CreateApiClient(roles: Roles.Scolarite);

    private static MultipartFormDataContent Upload(byte[] workbook)
    {
        var content = new ByteArrayContent(workbook);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        return new MultipartFormDataContent { { content, "file", "affectations.xlsx" } };
    }

    /// <summary>
    /// The canvas the endpoint hands out, with <paramref name="fill"/> applied to it — the operator's
    /// edit, made the way a spreadsheet makes it.
    /// </summary>
    private async Task<byte[]> CanvasAsync(Action<IXLWorksheet>? fill = null)
    {
        using var client = Admin();
        var response = await client.GetAsync($"/api/affectations/sheet/template?levelId={LevelId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the canvas is downloaded before it is filled");

        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        if (fill is null) return bytes;

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        fill(workbook.Worksheets.First());

        using var edited = new MemoryStream();
        workbook.SaveAs(edited);
        return edited.ToArray();
    }

    /// <summary>Fills the two date columns of every data line, the way the operator would.</summary>
    private static void FillDates(IXLWorksheet sheet)
    {
        var header = sheet.RowsUsed().First(r =>
            r.Cells().Any(c => c.GetString().Trim().Equals("Stage", StringComparison.OrdinalIgnoreCase)));

        int startColumn = Column(header, "Début");
        int endColumn = Column(header, "Fin");
        int serviceColumn = Column(header, "Service");

        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber()))
        {
            row.Cell(serviceColumn).Value = "Chirurgie A";
            row.Cell(startColumn).Value = Start.ToDateTime(TimeOnly.MinValue);
            row.Cell(endColumn).Value = End.ToDateTime(TimeOnly.MinValue);
        }
    }

    private static int Column(IXLRow header, string name) =>
        header.Cells().First(c => c.GetString().Trim()
            .StartsWith(name, StringComparison.OrdinalIgnoreCase)).Address.ColumnNumber;

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private Task<int> PeriodCountAsync() => _factory.QueryAsync(db => db.ServicePeriods.CountAsync());

    // ─── The round trip ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_canvas_goes_out_filled_comes_back_and_plans_the_promotion()
    {
        byte[] filled = await CanvasAsync(FillDates);

        using var client = Admin();

        using var preview = await client.PostAsync(
            $"/api/affectations/sheet/preview?levelId={LevelId}", Upload(filled));

        preview.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await BodyAsync(preview);
        report.GetProperty("willCreate").GetInt32().Should().Be(1);
        report.GetProperty("errorCount").GetInt32().Should().Be(0);
        report.GetProperty("canApply").GetBoolean().Should().BeTrue();

        using var applied = await client.PostAsync(
            $"/api/affectations/sheet?levelId={LevelId}&confirmedCount=1&confirmedDroppedPeriods=0",
            Upload(filled));

        applied.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PeriodCountAsync()).Should().Be(1);
    }

    /// <summary>
    /// ⚠ <b>An unedited canvas plans nothing and says so.</b> It is not a refusal: the document goes out
    /// with a line per (student, stage), so planning one stage means uploading a file whose other lines
    /// are blank. Refusing those would mean deleting hundreds of rows before every upload, and a file
    /// that is painful to re-send stops being re-sent.
    /// </summary>
    [Fact]
    public async Task An_unedited_canvas_plans_nothing_and_says_so_rather_than_refusing()
    {
        byte[] untouched = await CanvasAsync();

        using var client = Admin();
        using var preview = await client.PostAsync(
            $"/api/affectations/sheet/preview?levelId={LevelId}", Upload(untouched));

        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());

        var report = await BodyAsync(preview);
        report.GetProperty("errorCount").GetInt32().Should().Be(0);
        report.GetProperty("notPlanned").GetInt32().Should().Be(1);
        report.GetProperty("affectations").GetInt32().Should().Be(0);
        report.GetProperty("rows")[0].GetProperty("status").GetString().Should().Be("NotPlanned");

        using var applied = await client.PostAsync(
            $"/api/affectations/sheet?levelId={LevelId}&confirmedCount=0&confirmedDroppedPeriods=0",
            Upload(untouched));

        applied.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PeriodCountAsync()).Should().Be(0, "an untouched canvas writes nothing");
    }

    /// <summary>
    /// The control for the rule above: blank everywhere is « je n'y ai pas touché », half-filled is a
    /// mistake. Without this the skip would swallow a line somebody meant to plan.
    /// </summary>
    [Fact]
    public async Task A_service_with_no_dates_still_refuses_the_file()
    {
        byte[] half = await CanvasAsync(sheet =>
        {
            var header = sheet.RowsUsed().First(r =>
                r.Cells().Any(c => c.GetString().Trim().Equals("Stage", StringComparison.OrdinalIgnoreCase)));

            foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber()))
                row.Cell(Column(header, "Service")).Value = "Chirurgie A";
        });

        using var client = Admin();
        using var preview = await client.PostAsync(
            $"/api/affectations/sheet/preview?levelId={LevelId}", Upload(half));

        var report = await BodyAsync(preview);
        report.GetProperty("errorCount").GetInt32().Should().Be(1);
        report.GetProperty("canApply").GetBoolean().Should().BeFalse();
        report.GetProperty("rows")[0].GetProperty("status").GetString().Should().Be("MissingDates");
    }

    // ─── The counts, at the boundary ──────────────────────────────────────────

    [Fact]
    public async Task A_stale_count_is_refused_with_a_sentence_that_reaches_the_screen()
    {
        byte[] filled = await CanvasAsync(FillDates);

        using var client = Admin();
        using var response = await client.PostAsync(
            $"/api/affectations/sheet?levelId={LevelId}&confirmedCount=9&confirmedDroppedPeriods=0",
            Upload(filled));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a business refusal is never a 500, or the client discards the one sentence explaining it");

        var problem = await BodyAsync(response);
        problem.GetProperty("title").GetString().Should().Be("AffectationSheet.CountMismatch");
        problem.GetProperty("detail").GetString().Should().Contain("Relancez l'aperçu");

        (await PeriodCountAsync()).Should().Be(0, "a refusal writes nothing");
    }

    /// <summary>The validator, which only a request through the pipeline reaches.</summary>
    [Fact]
    public async Task A_negative_confirmed_count_is_a_bad_request()
    {
        byte[] filled = await CanvasAsync(FillDates);

        using var client = Admin();
        using var response = await client.PostAsync(
            $"/api/affectations/sheet?levelId={LevelId}&confirmedCount=-1&confirmedDroppedPeriods=0",
            Upload(filled));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PeriodCountAsync()).Should().Be(0);
    }

    // ─── The file itself ──────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Picking the wrong file is a user mistake, not a fault. Typed <c>Problem</c> it would surface as
    /// a 500 and the client would replace the message with « Une erreur serveur est survenue ».
    /// </summary>
    [Fact]
    public async Task A_file_that_is_not_a_workbook_is_a_bad_request_not_a_500()
    {
        using var client = Admin();
        using var response = await client.PostAsync(
            $"/api/affectations/sheet/preview?levelId={LevelId}",
            Upload(Encoding.UTF8.GetBytes("ceci n'est pas un classeur")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(response)).GetProperty("title").GetString()
            .Should().Be("AffectationSheet.Unreadable");
    }

    // ─── The controls ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Without this, every refusal assertion above would also pass on a route that 400s on
    /// everything — a typo in the path, a binding failure — and prove nothing.
    /// </summary>
    [Fact]
    public async Task The_route_refuses_a_request_that_does_not_say_which_promotion()
    {
        byte[] filled = await CanvasAsync(FillDates);

        using var client = Admin();
        using var response = await client.PostAsync("/api/affectations/sheet/preview", Upload(filled));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "levelId is required: an omitted promotion must not widen to all of them");
    }

    [Fact]
    public async Task An_anonymous_caller_never_reaches_the_act()
    {
        using var client = _factory.CreateAnonymousClient();
        using var response = await client.PostAsync(
            $"/api/affectations/sheet/preview?levelId={LevelId}",
            Upload(Encoding.UTF8.GetBytes("nope")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_student_is_refused_the_canvas()
    {
        using var client = _factory.CreateApiClient(roles: Roles.Student);
        using var response = await client.PostAsync(
            $"/api/affectations/sheet?levelId={LevelId}&confirmedCount=0&confirmedDroppedPeriods=0",
            Upload(await CanvasAsync(FillDates)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PeriodCountAsync()).Should().Be(0);
    }
}
