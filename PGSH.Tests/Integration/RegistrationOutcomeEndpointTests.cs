using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>POST registrations/{id}/outcome</c> and its <c>/reopen</c> through the real pipeline.
///
/// <para>⚠ What no handler test can see here is the <b>verdict itself</b>. It arrives as a JSON
/// string and is bound to <see cref="RegistrationStatus"/> by the globally registered
/// <c>JsonStringEnumConverter</c>; a handler test hands the enum over directly. If that binding ever
/// broke, every request would arrive carrying the default — <c>Pending</c> — and the route would
/// answer 204 while recording a verdict nobody pronounced. So the first test asserts the *stored*
/// status, not the status code.</para>
/// </summary>
public class RegistrationOutcomeEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int Level6 = 6;
    private const int Level7 = 7;
    private const int SevenYearText = 91;

    private const string FinalYearCne = "F13089613";
    private const string MidCursusCne = "M13089614";

    private readonly ApiFactory _factory;

    public RegistrationOutcomeEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// One seven-year text and two students under it: one in the 7ᵉ année, where « Diplômé » is
    /// legitimate, and one in the 6ᵉ, where it is not. That pair is the whole point — the final-year
    /// rule is asked per student from his own text, never from the level.
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
            Id = Level6, Label = "Sixième Année Médecine", Year = 6,
            AcademicProgram = AcademicProgram.Medecine,
        });
        db.Levels.Add(new Level
        {
            Id = Level7, Label = "Septième Année Médecine", Year = 7,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.CnpnVersions.Add(new CnpnVersion
        {
            Id = SevenYearText, Code = "2174.18", Label = "CNPN 2019 (7 ans)",
            AcademicProgram = AcademicProgram.Medecine, TotalYears = 7,
        });

        AddStudent(db, FinalYearCne, "Omar", "Idrissi", Level7);
        AddStudent(db, MidCursusCne, "Salma", "Tazi", Level6);
    });

    private static void AddStudent(ApplicationDbContext db, string cne, string first, string last, int levelId)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = last,
            Email = $"{cne.ToLowerInvariant()}@etu.test", CNE = cne, Appogee = $"AP{cne}",
            BacYear = "2019", AcademicProgram = AcademicProgram.Medecine,
        };
        student.AssignCnpnVersion(SevenYearText, isInferred: false);

        var registration = new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = levelId,
            StudentId = student.Id, Student = student,
            Status = RegistrationStatus.Active,
        };
        registration.StampCnpnVersion(SevenYearText, RegistrationCnpnSource.Backfilled);

        db.Users.Add(student);
        db.Registrations.Add(registration);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task<Guid> RegistrationIdAsync(string cne) => _factory.QueryAsync(db =>
        db.Registrations.Where(r => r.Student.CNE == cne).Select(r => r.Id).FirstAsync());

    private Task<Registration> StoredAsync(string cne) => _factory.QueryAsync(db =>
        db.Registrations.AsNoTracking().FirstAsync(r => r.Student.CNE == cne));

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    private Task<HttpResponseMessage> RecordAsync(
        HttpClient client, Guid registrationId, string outcome, string? motif = null) =>
        client.PostAsJsonAsync($"/api/registrations/{registrationId}/outcome", new { outcome, motif });

    // ─── The tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ The case this file exists for: « Redoublant » travels as the string <c>"Failed"</c> and has to
    /// arrive as the enum. The stored status is the assertion; a 204 proves only that something ran.
    /// </summary>
    [Fact]
    public async Task A_verdict_travels_as_a_string_and_lands_as_the_verdict()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await RecordAsync(
            client, await RegistrationIdAsync(MidCursusCne), "Failed", "Stage de chirurgie non validé");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await StoredAsync(MidCursusCne);
        stored.Status.Should().Be(RegistrationStatus.Failed);
        stored.OutcomeSource.Should().Be(RegistrationOutcomeSource.Declared);
        stored.OutcomeRecordedOn.Should().NotBeNull();
        stored.failureReasons.Should().NotBeNull();
    }

    /// <summary>
    /// A motif qualifies a decision that goes against the student; on a favourable one it has nothing
    /// to qualify. The route accepts it and the aggregate drops it, exactly as the canvas does.
    /// </summary>
    [Fact]
    public async Task A_favourable_verdict_keeps_no_motif()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await RecordAsync(
            client, await RegistrationIdAsync(MidCursusCne), "Validated", "collé par erreur");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await StoredAsync(MidCursusCne);
        stored.Status.Should().Be(RegistrationStatus.Validated);
        stored.failureReasons.Should().BeNull();
    }

    /// <summary>
    /// « Diplômé » on a year that is not the last of the student's own text. Asked from the CNPN
    /// stamped on the registration, so a level alone never answers it.
    /// </summary>
    [Fact]
    public async Task Graduating_before_the_last_year_refuses_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await RecordAsync(client, await RegistrationIdAsync(MidCursusCne), "Graduated");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TitleAsync(response)).Should().Be("Registrations.NotAFinalYear");

        var stored = await StoredAsync(MidCursusCne);
        stored.Status.Should().Be(RegistrationStatus.Active, "the refusal must precede the write");
        stored.OutcomeSource.Should().BeNull();
    }

    /// <summary>The control for the refusal above: in the 7ᵉ année the same verdict must go through.</summary>
    [Fact]
    public async Task Graduating_in_the_last_year_of_that_text_is_recorded()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await RecordAsync(client, await RegistrationIdAsync(FinalYearCne), "Graduated");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredAsync(FinalYearCne)).Status.Should().Be(RegistrationStatus.Graduated);
    }

    /// <summary>
    /// Re-opening names the verdict it withdrew — the caller has to be able to say what was undone —
    /// and clears the provenance with it, or the year would read as declared with no decision on it.
    /// </summary>
    [Fact]
    public async Task Reopening_says_which_verdict_it_withdrew()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);
        var registrationId = await RegistrationIdAsync(MidCursusCne);

        await RecordAsync(client, registrationId, "Failed", "PV corrigé");

        var response = await client.PostAsJsonAsync(
            $"/api/registrations/{registrationId}/outcome/reopen", new { reason = "PV corrigé" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("withdrawnOutcome").GetString().Should().Be("Failed");

        var stored = await StoredAsync(MidCursusCne);
        stored.Status.Should().Be(RegistrationStatus.Active);
        stored.OutcomeSource.Should().BeNull();
        stored.OutcomeRecordedOn.Should().BeNull();
    }

    /// <summary>
    /// Nothing to withdraw. Answering 200 here would tell the caller a verdict was taken back when
    /// none was ever pronounced.
    /// </summary>
    [Fact]
    public async Task Reopening_a_year_that_was_never_closed_is_refused()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);
        var registrationId = await RegistrationIdAsync(MidCursusCne);

        var response = await client.PostAsJsonAsync(
            $"/api/registrations/{registrationId}/outcome/reopen", new { reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("Registrations.NoOutcomeToReopen");
    }

    [Fact]
    public async Task Only_the_administrative_side_may_pronounce_a_verdict()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        var response = await RecordAsync(client, await RegistrationIdAsync(MidCursusCne), "Failed");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StoredAsync(MidCursusCne)).OutcomeSource.Should().BeNull();
    }

    [Fact]
    public async Task An_anonymous_caller_never_reaches_the_handler()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await RecordAsync(client, await RegistrationIdAsync(MidCursusCne), "Failed");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StoredAsync(MidCursusCne)).OutcomeSource.Should().BeNull();
    }

    /// <summary>
    /// The route constraint is <c>{id:guid}</c>, so a malformed id is a route that does not exist —
    /// answered by routing, never by the handler, and never as a 500.
    /// </summary>
    [Fact]
    public async Task A_malformed_registration_id_is_not_a_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync(
            "/api/registrations/not-a-guid/outcome", new { outcome = "Failed", motif = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
