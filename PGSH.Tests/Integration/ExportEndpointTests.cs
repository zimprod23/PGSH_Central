using System.Net;
using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Application.Abstractions.Authentication;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// The two export routes through the real pipeline.
///
/// <para>What a handler test cannot see and this can: whether the route is reachable at all, whether
/// <c>[AsParameters]</c> really binds every filter off the query string (it is the only binding in
/// the project that a handler test proves nothing about), whether the bytes come back as a
/// <b>download</b> rather than as JSON, and whether an anonymous caller is stopped before the handler
/// is ever asked.</para>
///
/// <para>⚠ <b>Every refusal is paired with the request that must still succeed.</b> A route that
/// 404s on everything — a typo in the path, a filter that binds to nothing — satisfies every refusal
/// assertion and proves nothing.</para>
/// </summary>
public class ExportEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int OtherYearId = 2;
    private const int ThirdYearLevelId = 3;
    private const int FifthYearLevelId = 5;
    private const int StageId = 1;

    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ApiFactory _factory;

    public ExportEndpointTests(ApiFactory factory) => _factory = factory;

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
        db.AcademicYears.Add(new AcademicYear
        {
            Id = OtherYearId, Label = "2024-2025",
            StartDate = new DateOnly(2024, 9, 1), EndDate = new DateOnly(2025, 8, 31),
        });

        var third = new Level
        {
            Id = ThirdYearLevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        };
        var fifth = new Level
        {
            Id = FifthYearLevelId, Label = "Cinquième Année Médecine", Year = 5,
            AcademicProgram = AcademicProgram.Medecine,
        };
        db.Levels.AddRange(third, fifth);

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Chirurgie", LevelId = ThirdYearLevelId,
            Coefficient = 2, DurationInDays = 44,
        });

        var group = new AcademicGroup
        {
            Id = 1, Label = "Groupe 1", GroupNumber = 1, RotationGroup = "A",
            AcademicYearId = YearId, LevelId = ThirdYearLevelId,
        };
        db.AcademicGroups.Add(group);

        db.Users.Add(Student("Amina", "Benali", "CNE0001", "AP0001"));
        db.Users.Add(Student("Sara", "Cherkaoui", "CNE0002", "AP0002"));

        db.Registrations.Add(Registration("CNE0001", ThirdYearLevelId, YearId, group.Id));
        db.Registrations.Add(Registration("CNE0002", FifthYearLevelId, YearId, null));

        Student Student(string first, string last, string cne, string appogee) => new()
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-0000000000{cne[^2..]}"),
            FirstName = first, LastName = last, CNE = cne, Appogee = appogee,
            Email = $"{first}.{last}@etu.ma".ToLowerInvariant(),
            BacYear = "2022", AcademicProgram = AcademicProgram.Medecine,
        };

        Registration Registration(string cne, int levelId, int yearId, int? groupId) => new()
        {
            Id = Guid.NewGuid(), AcademicYearId = yearId, LevelId = levelId,
            StudentId = Guid.Parse($"00000000-0000-0000-0000-0000000000{cne[^2..]}"),
            AcademicGroupId = groupId, Status = RegistrationStatus.Active,
        };
    });

    // ── students/export ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_anonymous_caller_gets_no_roll()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/students/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The control. Without it, a route that 404s on everything satisfies every assertion above.
    /// </summary>
    [Fact]
    public async Task Scolarite_downloads_an_xlsx()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.GetAsync("/api/students/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(Xlsx);
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".xlsx");
        (await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    /// <summary>
    /// ⚠ <c>[AsParameters]</c> binding is the half no handler test sees: a filter that silently fails
    /// to bind produces a correct-looking file covering the wrong population, and nothing anywhere
    /// says so. The file name is what the binding is read back through — it is built from the scope
    /// the handler actually resolved.
    /// </summary>
    [Fact]
    public async Task The_level_filter_binds_off_the_query_string()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.GetAsync($"/api/students/export?levelId={FifthYearLevelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName
            .Should().Contain("cinquieme-annee-medecine");
    }

    [Fact]
    public async Task An_unknown_level_is_refused_rather_than_exporting_the_whole_year()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.GetAsync("/api/students/export?levelId=4242");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── stages/assignments/export ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_anonymous_caller_gets_no_stage_record()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/stages/assignments/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Scolarite_downloads_the_stage_record_as_an_xlsx()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.GetAsync(
            $"/api/stages/assignments/export?levelId={ThirdYearLevelId}&onlyEvaluated=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(Xlsx);
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".xlsx");
    }

    [Fact]
    public async Task An_unknown_stage_is_refused()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.GetAsync("/api/stages/assignments/export?stageId=4242");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// ⚠ Sending no role header leaves the request authenticated but unprivileged — a handler that
    /// always allowed would be indistinguishable from one that checks, so this is the case that makes
    /// the success above mean something.
    /// </summary>
    [Fact]
    public async Task A_professor_is_refused_both_exports()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        (await client.GetAsync("/api/students/export")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/stages/assignments/export")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }
}
