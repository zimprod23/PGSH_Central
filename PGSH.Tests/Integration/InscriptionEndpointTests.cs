using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>POST inscription[/preview]</c> and <c>GET inscription/template</c> through the real pipeline.
///
/// <para>Everything specific to this endpoint lives outside the handler: the file arrives as
/// multipart and is parsed in the API layer, and <c>levelId</c> is a <b>required</b> query parameter
/// — the one thing a handler test cannot see, because it constructs the scope directly. The level is
/// not a filter here, it is the whole statement of which promotion these people are being inscribed
/// into, and nobody on the sheet holds a registration it could be read from instead.</para>
///
/// <para>⚠ <b>And this act creates people.</b> A refusal ordered after the writes returns the same
/// <c>Result.Failure</c> as one ordered before them and passes every handler test; only the store
/// tells them apart. So each refusal below asserts the refusal <b>and</b> that no student row was
/// created — with the request that must still succeed beside it, since a route that 400s on
/// everything satisfies every refusal assertion and proves nothing.</para>
/// </summary>
public class InscriptionEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int FirstYearLevelId = 3;
    private const int ThirdYearLevelId = 4;
    private const int WithdrawalLevelId = 5;

    private const string NewCne = "R13089613";
    private const string ExistingCne = "AP2200A";

    private readonly ApiFactory _factory;

    public InscriptionEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// A first year to inscribe into, a third year for the transfer case, « Retrait » for the marker
    /// case, and one student already on the books. No <c>CnpnVersion</c> is seeded, so the final-year
    /// rule stays out of the way — these tests are about the pipeline.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = YearId, Label = "2025-2026", IsCurrent = true,
            StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
        });

        db.Levels.Add(new Level
        {
            Id = FirstYearLevelId, Label = "Première Année Médecine", Year = 1,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Levels.Add(new Level
        {
            Id = ThirdYearLevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        // « Retrait » — a status the legacy base wore as a level. Nobody is inscribed into one.
        db.Levels.Add(new Level
        {
            Id = WithdrawalLevelId, Label = "Retrait", Year = 0,
            AcademicProgram = AcademicProgram.Medecine,
        });

        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = "Ali", LastName = "Amrani",
            Email = "ali.amrani@etu.test", CNE = ExistingCne, Appogee = $"AP{ExistingCne}",
            BacYear = "2022", AcademicProgram = AcademicProgram.Medecine,
        };

        db.Users.Add(student);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = FirstYearLevelId,
            StudentId = student.Id, Student = student,
        });
    });

    // ─── The request ──────────────────────────────────────────────────────────

    private static string Url(bool preview, int? levelId = FirstYearLevelId, int? confirmed = null)
    {
        var url = new StringBuilder(preview ? "/api/inscription/preview" : "/api/inscription");
        url.Append($"?academicYearId={YearId}");
        if (levelId is not null) url.Append($"&levelId={levelId}");
        if (confirmed is not null) url.Append($"&confirmedStudentCount={confirmed}");
        return url.ToString();
    }

    /// <summary>A one-row intake sheet, built the way the canvas produces it.</summary>
    private static MultipartFormDataContent Sheet(
        string cne = NewCne, string? institution = null, string? lastYear = null, string? reference = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Inscription");

        string[] headers =
        [
            "CNE", "Apogée", "Nom", "Prénom", "CIN", "E-mail", "Sexe", "Date de naissance",
            "Lieu de naissance", "Année du bac", "Série du bac", "Note d'accès", "Convention",
            "Établissement d'origine", "Pays d'origine", "Dernière année suivie",
            "Référence d'équivalence", "Date d'équivalence",
        ];

        for (int i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        sheet.Cell(2, 1).Value = cne;
        sheet.Cell(2, 2).Value = $"AP{cne}";
        sheet.Cell(2, 3).Value = "Bennani";
        sheet.Cell(2, 4).Value = "Sara";
        sheet.Cell(2, 6).Value = $"{cne}@um5.ac.ma".ToLowerInvariant();
        sheet.Cell(2, 7).Value = "F";
        sheet.Cell(2, 10).Value = "2025";
        sheet.Cell(2, 11).Value = "SVT";

        if (institution is not null) sheet.Cell(2, 14).Value = institution;
        if (lastYear is not null) sheet.Cell(2, 16).Value = lastYear;
        if (reference is not null) sheet.Cell(2, 17).Value = reference;

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return FileContent(buffer.ToArray(), "inscription.xlsx");
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string name)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, "file", name);
        return content;
    }

    private Task<int> StudentCountAsync() => _factory.QueryAsync(db => db.Students.CountAsync());

    private Task<int> RegistrationCountAsync() =>
        _factory.QueryAsync(db => db.Registrations.CountAsync());

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    // ─── The control ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Written first and deliberately. A route that refuses everything satisfies every assertion
    /// below and proves nothing; this is the request that must still go through.
    /// </summary>
    [Fact]
    public async Task A_first_year_intake_sheet_creates_the_student_and_his_registration()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: false, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await StudentCountAsync()).Should().Be(2, "the seeded student plus the one just inscribed");

        var registered = await _factory.QueryAsync(db => db.Registrations
            .AnyAsync(r => r.Student.CNE == NewCne && r.LevelId == FirstYearLevelId
                        && r.AcademicYearId == YearId));

        registered.Should().BeTrue();
    }

    [Fact]
    public async Task The_preview_reports_the_plan_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: true), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("willCreateStudents").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("newEntrants").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("canApply").GetBoolean().Should().BeTrue();

        (await StudentCountAsync()).Should().Be(1, "only the seeded student");
    }

    // ─── The level is required, and it is the guard ───────────────────────────

    /// <summary>
    /// ⚠ Nothing below the endpoint can see this. Omitted, model binding is what has to refuse — and
    /// if it did not, an intake file would be planned against whatever level a default resolved to.
    /// </summary>
    [Fact]
    public async Task An_upload_with_no_level_is_refused_and_creates_nobody()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: false, levelId: null, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StudentCountAsync()).Should().Be(1);
        (await RegistrationCountAsync()).Should().Be(1);
    }

    /// <summary>« Retrait » has no stages, no cohortes and nobody to rotate.</summary>
    [Fact]
    public async Task A_level_that_is_not_a_promotion_is_refused_and_creates_nobody()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(
            Url(preview: false, levelId: WithdrawalLevelId, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TitleAsync(response)).Should().Be("Inscription.NotAPromotion");
        (await StudentCountAsync()).Should().Be(1);
    }

    // ─── Creating people is confirmed by a number ─────────────────────────────

    /// <summary>
    /// The confirmation is a number the caller echoes back from the preview, so the endpoint has to
    /// receive it. Omitted — which is what a caller that never ran the simulation sends — the apply is
    /// refused and no identity is created.
    /// </summary>
    [Fact]
    public async Task An_apply_with_no_confirmation_creates_nobody()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: false), content);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("Inscription.CreationsNotConfirmed");
        (await StudentCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_confirmation_that_does_not_match_creates_nobody()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: false, confirmed: 99), content);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StudentCountAsync()).Should().Be(1);
    }

    // ─── Arriving from another faculty ────────────────────────────────────────

    [Fact]
    public async Task A_newcomer_above_the_first_year_without_a_provenance_creates_nobody()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(
            Url(preview: false, levelId: ThirdYearLevelId, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TitleAsync(response)).Should().Be("Inscription.Rejected");
        (await StudentCountAsync()).Should().Be(1);
    }

    /// <summary>
    /// The provenance columns travel through the parser, so this is the only place their headers are
    /// actually exercised — a renamed column would leave the équivalence silently unread, and the row
    /// would then be refused for a reason the file does not contain.
    /// </summary>
    [Fact]
    public async Task A_transfer_with_its_provenance_is_accepted_and_the_equivalence_stored()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet(institution: "FMP Casablanca", lastYear: "2", reference: "Arrêté 12/2026");
        var response = await client.PostAsync(
            Url(preview: false, levelId: ThirdYearLevelId, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var origin = await _factory.QueryAsync(db => db.PriorEnrolments.SingleAsync());
        origin.Institution.Should().Be("FMP Casablanca");
        origin.LastLevelYearCompleted.Should().Be(2);
        origin.EquivalenceReference.Should().Be("Arrêté 12/2026");
    }

    // ─── Idempotence ──────────────────────────────────────────────────────────

    /// <summary>
    /// This act creates identities, so the file has to survive being re-sent — scolarité appends the
    /// late arrivals and uploads the same sheet again.
    /// </summary>
    [Fact]
    public async Task Re_uploading_the_same_sheet_creates_nobody_a_second_time()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var first = Sheet();
        (await client.PostAsync(Url(preview: false, confirmed: 1), first))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Second time round nobody is created, so no confirmation is owed either.
        using var again = Sheet();
        var response = await client.PostAsync(Url(preview: false), again);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StudentCountAsync()).Should().Be(2);
        (await RegistrationCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_student_already_registered_this_year_is_skipped_not_duplicated()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet(cne: ExistingCne);
        var response = await client.PostAsync(Url(preview: false), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("alreadyRegistered").GetInt32().Should().Be(1);

        (await StudentCountAsync()).Should().Be(1);
        (await RegistrationCountAsync()).Should().Be(1);
    }

    // ─── Who may do it ────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Sending no header leaves the request anonymous, which is the only way to tell « allowed »
    /// from « never checked ».
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_cannot_inscribe_anyone()
    {
        using var client = _factory.CreateAnonymousClient();

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: false, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StudentCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_caller_who_is_not_the_administration_cannot_inscribe_anyone()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: false, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TitleAsync(response)).Should().Be("Inscription.NotAllowed");
        (await StudentCountAsync()).Should().Be(1);
    }

    // ─── The canvas ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_template_is_a_workbook_cut_for_the_promotion_it_was_asked_for()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.GetAsync(
            $"/api/inscription/template?levelId={ThirdYearLevelId}&academicYearId={YearId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));

        workbook.Worksheet("Inscription").Cell(1, 1).GetString().Should().Be("CNE");

        // Above the first year the provenance is required, and the canvas has to say so.
        string instructions = string.Join(
            "\n", workbook.Worksheet("Mode d'emploi").RangeUsed()!.Cells().Select(c => c.GetString()));

        instructions.Should().Contain("OBLIGATOIRE");
    }

    /// <summary>A workbook we cannot open is a bad request, not a 500.</summary>
    [Fact]
    public async Task An_upload_that_is_not_a_workbook_is_refused_as_a_bad_request()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = FileContent(Encoding.UTF8.GetBytes("ceci n'est pas un classeur"), "notes.txt");
        var response = await client.PostAsync(Url(preview: true), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TitleAsync(response)).Should().Be("Inscription.SheetUnreadable");
    }

    // ─── One student at a time ────────────────────────────────────────────────

    /// <summary>
    /// The escape hatch. It binds a JSON body rather than a file, so nothing the multipart tests cover
    /// says anything about it — and every value arrives as text, exactly as a sheet cell would, so the
    /// form and the file cannot disagree about what « 03/09/2006 » means.
    /// </summary>
    [Fact]
    public async Task One_student_can_be_inscribed_from_a_form()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/inscription/student", new
        {
            levelId = ThirdYearLevelId,
            academicYearId = YearId,
            cne = "T99001",
            lastName = "Alaoui",
            firstName = "Omar",
            dateOfBirth = "03/09/2006",
            originInstitution = "FMP Casablanca",
            originLastYearCompleted = "2",
            equivalenceReference = "Arrêté 12/2026",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("action").GetString().Should().Be("TransferIn");
        doc.RootElement.GetProperty("createsStudent").GetBoolean().Should().BeTrue();

        (await StudentCountAsync()).Should().Be(2);

        var born = await _factory.QueryAsync(db => db.Students
            .Where(s => s.CNE == "T99001").Select(s => s.DateOfBirth).SingleAsync());

        born.Should().Be(new DateOnly(2006, 9, 3));
        (await _factory.QueryAsync(db => db.PriorEnrolments.CountAsync())).Should().Be(1);
    }

    /// <summary>
    /// ⚠ The refusal names the field, not a count. « 1 ligne en erreur » is what a file needs and
    /// explains nothing on a form.
    /// </summary>
    [Fact]
    public async Task A_form_inscription_missing_its_provenance_is_refused_by_name_and_creates_nobody()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/inscription/student", new
        {
            levelId = ThirdYearLevelId,
            academicYearId = YearId,
            cne = "T99002",
            lastName = "Alaoui",
            firstName = "Omar",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TitleAsync(response)).Should().Be("Inscription.OriginRequired");
        (await StudentCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_form_inscription_by_a_caller_who_is_not_the_administration_creates_nobody()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        var response = await client.PostAsJsonAsync("/api/inscription/student", new
        {
            levelId = FirstYearLevelId,
            academicYearId = YearId,
            cne = "T99003",
            lastName = "Bennani",
            firstName = "Sara",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StudentCountAsync()).Should().Be(1);
    }
}
