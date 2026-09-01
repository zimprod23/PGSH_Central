using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using ClosedXML.Excel;
using FluentAssertions;
using Level = PGSH.Domain.Common.Utils.Level;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Domain.Registrations;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>POST deliberation[/preview]</c> through the real pipeline.
///
/// <para>Everything specific to this endpoint lives outside the handler: the file arrives as multipart
/// and is parsed in the API layer, the scope arrives as query parameters, and
/// <c>defaultUnlistedToAdmis</c> is a <b>bool that decides whether silence is a verdict</b>. A handler
/// test constructs the scope directly and so can never catch that flag failing to bind — and the
/// failure is silent in both directions: unbound, an exceptions file records three rows and admits
/// nobody; bound when it should not be, a nominative canvas closes the whole year.</para>
/// </summary>
public class DeliberationEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int LevelId = 3;

    private const string NamedCne = "R13089613";
    private const string SilentCne = "AP2200A";

    private readonly ApiFactory _factory;

    public DeliberationEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Two students of one promotion: one the file will name, one it never will. No <c>CnpnVersion</c>
    /// is seeded, so no year counts as anyone's last and the final-year rule stays out of the way —
    /// these tests are about the pipeline, not about the default's arithmetic.
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
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        AddStudent(db, NamedCne, "Sara", "Bennani");
        AddStudent(db, SilentCne, "Ali", "Amrani");
    });

    private static void AddStudent(ApplicationDbContext db,
        string cne, string firstName, string lastName)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = firstName, LastName = lastName,
            Email = $"{cne}@etu.test".ToLowerInvariant(),
            CNE = cne, Appogee = $"AP{cne}", BacYear = "2022",
            AcademicProgram = AcademicProgram.Medecine,
        };

        db.Users.Add(student);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = LevelId,
            StudentId = student.Id, Student = student,
        });
    }

    // ─── The request ──────────────────────────────────────────────────────────

    private static string Url(bool preview, bool defaults, int? confirmed = null)
    {
        var url = new StringBuilder(preview ? "/api/deliberation/preview" : "/api/deliberation");
        url.Append($"?academicYearId={YearId}&levelId={LevelId}");
        if (defaults) url.Append("&defaultUnlistedToAdmis=true");
        if (confirmed is not null) url.Append($"&confirmedDefaultCount={confirmed}");
        return url.ToString();
    }

    /// <summary>A one-row exceptions sheet naming <see cref="NamedCne"/>, built the way a jury would.</summary>
    private static MultipartFormDataContent Sheet(string decision = "Redoublant")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Déliberation");
        sheet.Cell(1, 1).Value = "CNE";
        sheet.Cell(1, 2).Value = "Apogée";
        sheet.Cell(1, 3).Value = "Décision";
        sheet.Cell(1, 4).Value = "Motif";
        sheet.Cell(2, 1).Value = NamedCne;
        sheet.Cell(2, 3).Value = decision;

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return FileContent(buffer.ToArray(), "deliberation.xlsx");
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

    private async Task<string?> StatusOfAsync(string cne) => await _factory.QueryAsync(db =>
        db.Registrations
            .Where(r => r.Student.CNE == cne)
            .Select(r => r.OutcomeSource == null ? null : r.Status.ToString())
            .FirstOrDefaultAsync());

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    // ─── The tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ The point of the file. The same upload, twice, differing only by a query flag — and the
    /// student nobody named is the one that tells them apart. Nothing below the endpoint can see this.
    /// </summary>
    [Fact]
    public async Task Silence_is_a_verdict_only_when_the_flag_is_actually_sent()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var withoutDefaults = Sheet();
        var plain = await client.PostAsync(Url(preview: false, defaults: false), withoutDefaults);

        plain.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(NamedCne)).Should().Be(nameof(RegistrationStatus.Failed));
        (await StatusOfAsync(SilentCne)).Should().BeNull("a nominative canvas leaves the unnamed alone");

        using var withDefaults = Sheet();
        var exceptions = await client.PostAsync(
            Url(preview: false, defaults: true, confirmed: 1), withDefaults);

        exceptions.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(SilentCne)).Should().Be(nameof(RegistrationStatus.Validated));
    }

    /// <summary>
    /// The confirmation is a number the caller echoes back, so the endpoint has to receive it. Sent
    /// wrong — the state changed since the simulation — the apply is refused and writes nothing.
    /// </summary>
    [Fact]
    public async Task A_confirmation_that_does_not_match_refuses_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(
            Url(preview: false, defaults: true, confirmed: 99), content);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("Deliberation.DefaultsNotConfirmed");

        // ⚠ Both of them: the guard sits after the plan is built, so a version of it ordered after the
        // writes would return this same failure with the year already closed behind it.
        (await StatusOfAsync(NamedCne)).Should().BeNull();
        (await StatusOfAsync(SilentCne)).Should().BeNull();
    }

    /// <summary>Omitted entirely, which is what a caller that never read the preview would send.</summary>
    [Fact]
    public async Task An_apply_with_no_confirmation_at_all_is_refused()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: false, defaults: true), content);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StatusOfAsync(SilentCne)).Should().BeNull();
    }

    /// <summary>
    /// The dry run is a <c>POST</c> that must write nothing — the one property distinguishing it from
    /// the apply, and the one a shared route would quietly lose.
    /// </summary>
    [Fact]
    public async Task The_preview_writes_nothing()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = Sheet();
        var response = await client.PostAsync(Url(preview: true, defaults: true), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(NamedCne)).Should().BeNull();
        (await StatusOfAsync(SilentCne)).Should().BeNull();
    }

    /// <summary>
    /// A file that is not a workbook is the user picking the wrong file. Only the endpoint sees a file
    /// at all, so nothing below it can prove this is a 400 and not an unhandled exception.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_not_a_workbook_is_a_bad_request_not_a_crash()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var content = FileContent("ceci n'est pas un classeur"u8.ToArray(), "notes.txt");
        var response = await client.PostAsync(Url(preview: true, defaults: true), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TitleAsync(response)).Should().Be("Deliberation.SheetUnreadable");
    }

    /// <summary>
    /// An empty file is refused even under the default — a workbook whose headers did not match parses
    /// to zero rows too, and that is indistinguishable from a file saying "promote everyone".
    /// </summary>
    [Fact]
    public async Task An_empty_sheet_is_refused_rather_than_admitting_the_promotion()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Déliberation");
        sheet.Cell(1, 1).Value = "CNE";
        sheet.Cell(1, 3).Value = "Décision";
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        using var content = FileContent(buffer.ToArray(), "vide.xlsx");
        var response = await client.PostAsync(
            Url(preview: false, defaults: true, confirmed: 2), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusOfAsync(SilentCne)).Should().BeNull();
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_close_a_year()
    {
        using var client = _factory.CreateAnonymousClient();

        using var content = Sheet();
        var response = await client.PostAsync(
            Url(preview: false, defaults: true, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StatusOfAsync(NamedCne)).Should().BeNull();
    }

    /// <summary>
    /// Authenticated but not administrative — a professor, say. Distinct from the anonymous case: one
    /// is "who are you", the other is "not you". Only the pipeline runs <c>ExecutionAuthorizer</c>
    /// against a real principal, so this is the only place the difference is visible.
    /// </summary>
    [Fact]
    public async Task An_authenticated_caller_who_is_not_administrative_is_forbidden()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        using var content = Sheet();
        var response = await client.PostAsync(
            Url(preview: false, defaults: true, confirmed: 1), content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusOfAsync(NamedCne)).Should().BeNull();
    }
}
