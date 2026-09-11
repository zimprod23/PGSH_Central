using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>GET services/promotion-fit</c> through the real pipeline.
///
/// <para>⚠ <b>What a handler test cannot see here.</b> The query binds from the query string through
/// <c>[AsParameters]</c> — two optional ints, so a typo in a name binds to nothing and the route
/// silently answers about the whole year instead of the promotion asked about. And the two refusals
/// have to arrive as <b>400</b> and <b>404</b> with their sentences: typed <c>Problem</c> they would
/// be 500s, and the client discards <c>detail</c> above 500.</para>
/// </summary>
public class PromotionFitEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int MedLevel = 3;
    private const int Retrait = 90;
    private const int StageId = 1;
    private const int HospitalId = 1;
    private const int ServiceId = 10;

    private readonly ApiFactory _factory;

    public PromotionFitEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// One promotion of four students on a single 15-day stage with one service of 20 — comfortable —
    /// plus « Retrait », which is a marker and not a promotion.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = YearId, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31),
            IsCurrent = true,
        });

        db.Levels.Add(new Level
        {
            Id = MedLevel, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Levels.Add(new Level { Id = Retrait, Label = "Retrait", Year = 0 });

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Chirurgie", LevelId = MedLevel,
            Coefficient = 2, DurationInDays = 15,
        });

        var hospital = new Hospital { Id = HospitalId, Name = "CHU Ibn Sina", City = "Rabat" };
        db.Hospitals.Add(hospital);
        db.Services.Add(new Service
        {
            Id = ServiceId, Name = "Cardiologie", Description = "",
            HospitalId = hospital.Id, Hospital = hospital, Capacity = 20,
        });
        db.StageAllowedServices.Add(new StageAllowedService
        {
            StageId = StageId, ServiceId = ServiceId, Rank = 1,
        });

        for (int i = 0; i < 4; i++)
        {
            var student = new Student
            {
                Id = Guid.NewGuid(), FirstName = $"E{i}", LastName = "Test",
                Email = $"e{i}@etu.ma", Appogee = $"AP{i:0000}", BacYear = "2023",
                AcademicProgram = AcademicProgram.Medecine,
            };
            db.Users.Add(student);
            db.Registrations.Add(new Registration
            {
                Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = MedLevel,
                StudentId = student.Id, Student = student,
            });
        }
    });

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    /// <summary>The control: the route answers, and the year it answers about is the current one.</summary>
    [Fact]
    public async Task The_panel_reads_the_current_year_when_none_is_given()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/services/promotion-fit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await BodyAsync(response);
        body.GetProperty("academicYearLabel").GetString().Should().Be("2026-2027");

        var promotion = body.GetProperty("promotions").EnumerateArray().Should().ContainSingle().Subject;
        promotion.GetProperty("students").GetInt32().Should().Be(4);
        promotion.GetProperty("state").GetString().Should().Be("Fits",
            "enums travel as strings — JsonStringEnumConverter is registered globally");

        var stage = promotion.GetProperty("stages").EnumerateArray().Single();
        stage.GetProperty("studentsAtOnce").GetInt32().Should().Be(4, "one stage, so the axis is one column");
        stage.GetProperty("places").GetInt32().Should().Be(20);
        stage.GetProperty("margin").GetInt32().Should().Be(16);
    }

    /// <summary>⚠ The filter has to actually bind — an unbound parameter widens silently.</summary>
    [Fact]
    public async Task The_level_filter_binds_from_the_query_string()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/services/promotion-fit?levelId={MedLevel}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(response)).GetProperty("scope").GetString()
            .Should().Contain("Troisième Année Médecine");
    }

    /// <summary>
    /// « Retrait » is refused, and the refusal is a <b>400 with its sentence</b> — not a 500, whose
    /// detail the client throws away.
    /// </summary>
    /// <remarks>
    /// ⚠ A single <c>Error.Validation</c> carries no <c>errors[]</c> at all — that array is filled
    /// only for a <c>ValidationError</c>, i.e. the aggregate <c>ValidationPipelineBehavior</c> raises.
    /// Its code is in <c>title</c> and its sentence in <c>detail</c>, which is exactly the asymmetry
    /// <c>StageDetailPage.extractErrorCode</c> got wrong by reading only <c>errors[0].code</c>.
    /// </remarks>
    [Fact]
    public async Task Retrait_is_refused_with_a_sentence()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/services/promotion-fit?levelId={Retrait}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await BodyAsync(response);
        problem.GetProperty("title").GetString().Should().Be("Levels.NotAPromotion");
        problem.GetProperty("detail").GetString().Should().Contain("n'est pas une promotion");
    }

    /// <summary>A year nobody is registered in is a 404 that says what to do, not an empty panel.</summary>
    [Fact]
    public async Task A_year_with_no_promotion_is_a_not_found_that_explains_itself()
    {
        await _factory.SeedAsync(db => db.AcademicYears.Add(new AcademicYear
        {
            Id = 2, Label = "2027-2028",
            StartDate = new DateOnly(2027, 9, 1), EndDate = new DateOnly(2028, 8, 31),
        }));

        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/services/promotion-fit?academicYearId=2");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(response)).GetProperty("detail").GetString()
            .Should().Contain("2027-2028");
    }
}
