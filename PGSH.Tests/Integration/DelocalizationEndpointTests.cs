using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using Xunit;
using PGSH.Application.Abstractions.Authentication;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// The délocalisation routes through the real pipeline: <c>stages/delocalize</c>,
/// <c>stages/delocalize/bulk/preview</c>, <c>stages/delocalize/bulk</c> and
/// <c>stages/delocalize/cancel</c>.
///
/// <para>What only this suite can see: the validator, which never runs in a handler test — the
/// délocalisation's dates and its verdict are checked in
/// <c>ValidationPipelineBehavior</c>, so a rule that refuses rows the base already holds would be
/// invisible everywhere else. And that a refusal happens <b>before</b> the write: the bulk act
/// deletes rotations, so a guard ordered after it returns the same failure and passes a handler
/// test.</para>
///
/// <para>⚠ Every refusal here is paired with the request that must still succeed. A route that 400s
/// on everything satisfies each refusal assertion and proves nothing.</para>
/// </summary>
public class DelocalizationEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId    = 3;
    private const int StageId    = 1;
    private const int YearId     = 1;
    private const int GroupId    = 10;
    private const int CohortId   = 10;
    private const int HospitalId = 1;
    private const int HomeId     = 20;
    private const int ExternalId = 21;

    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly DateOnly End   = new(2026, 3, 29);

    private readonly ApiFactory _factory;
    private Guid _registrationId;
    private string _appogee = "";

    public DelocalizationEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync()
    {
        _registrationId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        _appogee = "AP" + Random.Shared.Next(100000, 999999);

        await _factory.SeedAsync(db =>
        {
            db.AcademicYears.Add(new AcademicYear
            {
                Id = YearId, Label = "2025-2026", IsCurrent = true,
                StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 7, 31),
            });

            db.Levels.Add(new Level
            {
                Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
                AcademicProgram = AcademicProgram.Medecine,
            });

            var stage = new Stage
            {
                Id = StageId, Name = "Chirurgie", LevelId = LevelId,
                Coefficient = 2, DurationInDays = 28,
            };
            db.Stages.Add(stage);

            var hospital = new Hospital { Id = HospitalId, Name = "CHU Ibn Sina", City = "Rabat" };
            db.Hospitals.Add(hospital);

            db.Services.Add(new Service
            {
                Id = HomeId, Name = "Cardiologie", Description = "",
                HospitalId = hospital.Id, Hospital = hospital, Capacity = 20,
            });

            db.Services.Add(new Service
            {
                Id = ExternalId, Name = "Stage hors CHU — Kénitra", Description = "",
                HospitalId = hospital.Id, Hospital = hospital, Capacity = 20, IsExternal = true,
            });

            var group = db.SeedGroup(
                GroupId, 10, academicYearId: YearId, levelId: LevelId, label: "G10");

            db.Cohorts.Add(TestHarness.NewCohort(CohortId, stage, group, "Chirurgie · G10"));

            db.Students.Add(new Student
            {
                Id = studentId, FirstName = "Omar", LastName = "Tazi",
                Email = $"omar.{studentId:N}@pgsh.ma", Appogee = _appogee, BacYear = "2020",
            });

            db.Registrations.Add(new Registration
            {
                Id = _registrationId, StudentId = studentId, AcademicYearId = YearId,
                LevelId = LevelId, AcademicGroupId = GroupId,
                Status = RegistrationStatus.Active, RegistrationDate = DateTime.UtcNow,
            });
        });
    }

    private Task<int> DelocalizedPeriodCountAsync() =>
        _factory.QueryAsync(db => db.ServicePeriods.CountAsync(p => p.IsDelocalized));

    private object BulkBody(int confirmed, object targets) => new
    {
        stageId        = StageId,
        serviceId      = ExternalId,
        reason         = "Saturation — accueil à Kénitra",
        targets,
        confirmedCount = confirmed,
        startDate      = Start.ToString("yyyy-MM-dd"),
        endDate        = End.ToString("yyyy-MM-dd"),
    };

    // ─── the single act ───────────────────────────────────────────────────────

    [Fact]
    public async Task Delocalizing_one_student_is_accepted()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize", new
        {
            registrationId = _registrationId,
            stageId        = StageId,
            serviceId      = ExternalId,
            reason         = "Stage effectué à Kénitra",
            startDate      = Start.ToString("yyyy-MM-dd"),
            endDate        = End.ToString("yyyy-MM-dd"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DelocalizedPeriodCountAsync()).Should().Be(1);
    }

    // ⚠ Only the pipeline runs the validator. A motif is what the dossier shows years later.
    [Fact]
    public async Task A_delocalization_without_a_motif_is_refused_and_nothing_is_written()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize", new
        {
            registrationId = _registrationId,
            stageId        = StageId,
            serviceId      = ExternalId,
            reason         = "",
            startDate      = Start.ToString("yyyy-MM-dd"),
            endDate        = End.ToString("yyyy-MM-dd"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DelocalizedPeriodCountAsync()).Should().Be(0);
    }

    // ⚠ The rule that used to refuse an omitted date. Neither date means « take the stage's own
    // window », which is the ordinary case rather than an omission to correct — so this must pass
    // the validator and then be refused by the handler, by name, for having no créneaux to read.
    [Fact]
    public async Task Omitting_both_dates_passes_the_validator_and_is_answered_by_the_handler()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize", new
        {
            registrationId = _registrationId,
            stageId        = StageId,
            serviceId      = ExternalId,
            reason         = "Stage effectué à Kénitra",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "the stage has no créneaux this year");
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("créneau");
    }

    [Fact]
    public async Task One_date_without_the_other_is_refused_by_the_validator()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize", new
        {
            registrationId = _registrationId,
            stageId        = StageId,
            serviceId      = ExternalId,
            reason         = "Stage effectué à Kénitra",
            startDate      = Start.ToString("yyyy-MM-dd"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DelocalizedPeriodCountAsync()).Should().Be(0);
    }

    // The verdict's own rules run through the same pipeline: a numeric mode with no note is a fiche
    // nobody can read, and it must not be storable by this route when the chef's route refuses it.
    [Fact]
    public async Task A_numeric_verdict_with_no_note_is_refused()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize", new
        {
            registrationId = _registrationId,
            stageId        = StageId,
            serviceId      = ExternalId,
            reason         = "Stage effectué à Kénitra",
            startDate      = Start.ToString("yyyy-MM-dd"),
            endDate        = End.ToString("yyyy-MM-dd"),
            verdict        = new { mode = "Numeric" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DelocalizedPeriodCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_numeric_verdict_with_a_note_is_accepted_and_kept()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize", new
        {
            registrationId = _registrationId,
            stageId        = StageId,
            serviceId      = ExternalId,
            reason         = "Stage effectué à Kénitra",
            startDate      = Start.ToString("yyyy-MM-dd"),
            endDate        = End.ToString("yyyy-MM-dd"),
            verdict        = new { mode = "Numeric", totalScore = 15.5m },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var score = await _factory.QueryAsync(db => db.ServiceEvaluation
            .Select(e => e.TotalScore)
            .FirstAsync());

        score.Should().Be(15.5m);
    }

    // ─── the bulk act ─────────────────────────────────────────────────────────

    [Fact]
    public async Task The_bulk_preview_writes_nothing()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize/bulk/preview", new
        {
            stageId   = StageId,
            serviceId = ExternalId,
            targets   = new { academicGroupIds = new[] { GroupId } },
            startDate = Start.ToString("yyyy-MM-dd"),
            endDate   = End.ToString("yyyy-MM-dd"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WillDelocalize");
        (await DelocalizedPeriodCountAsync()).Should().Be(0, "a preview is a read");
    }

    [Fact]
    public async Task A_roster_and_a_pasted_identifier_are_applied_together()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize/bulk",
            BulkBody(1, new { academicGroupIds = new[] { GroupId }, identifiers = new[] { _appogee } }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DelocalizedPeriodCountAsync()).Should().Be(1, "the same student named twice is one row");
    }

    // ⚠ The guard the whole act hangs on, and only this suite proves it refuses before the write.
    [Fact]
    public async Task A_count_that_does_not_match_the_preview_is_refused_and_nothing_is_written()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize/bulk",
            BulkBody(7, new { academicGroupIds = new[] { GroupId } }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await DelocalizedPeriodCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_bulk_delocalization_without_a_motif_is_refused()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize/bulk", new
        {
            stageId        = StageId,
            serviceId      = ExternalId,
            reason         = "",
            targets        = new { academicGroupIds = new[] { GroupId } },
            confirmedCount = 1,
            startDate      = Start.ToString("yyyy-MM-dd"),
            endDate        = End.ToString("yyyy-MM-dd"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DelocalizedPeriodCountAsync()).Should().Be(0);
    }

    // ─── the way back ─────────────────────────────────────────────────────────

    /// <summary>
    /// The window each row is dated by has to survive the boundary — the dates and the provenance
    /// both.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>This is the assertion class that caught the last one.</b> A field computed by a
    /// planner, carried in its result and then dropped at the edge is invisible to every handler
    /// test: the arranger's <c>PinnedCellsKept</c> and <c>ReservedServices</c> were computed
    /// correctly and reached the screen as nothing, because a second result shape at the API had
    /// never been extended. Here the endpoint returns the report itself, and this test is what says
    /// so out loud.</para>
    ///
    /// <para>The dates are omitted deliberately: that is the path where PGSH derives them, and the
    /// one the defect lived on.</para>
    /// </remarks>
    [Fact]
    public async Task The_preview_carries_each_rows_own_window_across_the_boundary()
    {
        // A stage crossed in two périodes, and this cohorte passing in the first only.
        await _factory.SeedAsync(db =>
        {
            db.StageSlots.Add(TestHarness.NewSlot(1, StageId, YearId, 1, Start, End));
            db.StageSlots.Add(TestHarness.NewSlot(
                2, StageId, YearId, 2, new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 31)));
            db.CohortSlotAssignments.Add(new CohortSlotAssignment
            {
                Id = 1, CohortId = CohortId, StageSlotId = 1, ServiceId = HomeId,
            });
        });

        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize/bulk/preview", new
        {
            stageId   = StageId,
            serviceId = ExternalId,
            targets   = new { academicGroupIds = new[] { GroupId } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        report.GetProperty("applicableCount").GetInt32().Should().Be(1);
        report.GetProperty("distinctWindowCount").GetInt32().Should().Be(1);
        report.GetProperty("stageWideWindowCount").GetInt32().Should().Be(0);

        var row = report.GetProperty("rows").EnumerateArray().Single();

        // ⚠ The cohorte's own passage, not min/max over both créneaux — which would end 31/05.
        row.GetProperty("startDate").GetString().Should().Be("2026-03-02");
        row.GetProperty("endDate").GetString().Should().Be("2026-03-29");
        row.GetProperty("windowSource").GetString().Should().Be("Cohort");
        row.GetProperty("windowIsStageWide").GetBoolean().Should().BeFalse();

        // The control: the same route with the operator's own dates still answers, and says whose
        // dates they are. A route that 400s on everything would satisfy nothing above.
        var named = await client.PostAsJsonAsync("/api/stages/delocalize/bulk/preview", new
        {
            stageId   = StageId,
            serviceId = ExternalId,
            targets   = new { academicGroupIds = new[] { GroupId } },
            startDate = "2026-04-06",
            endDate   = "2026-05-03",
        });

        named.StatusCode.Should().Be(HttpStatusCode.OK);

        var namedReport = await named.Content.ReadFromJsonAsync<JsonElement>();
        namedReport.GetProperty("rows").EnumerateArray().Single()
            .GetProperty("windowSource").GetString().Should().Be("Named");
        namedReport.GetProperty("startDate").GetString().Should().Be("2026-04-06");
    }

    [Fact]
    public async Task A_delocalization_can_be_cancelled_through_the_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        await client.PostAsJsonAsync("/api/stages/delocalize/bulk",
            BulkBody(1, new { academicGroupIds = new[] { GroupId } }));

        (await DelocalizedPeriodCountAsync()).Should().Be(1);

        var response = await client.PostAsJsonAsync("/api/stages/delocalize/cancel", new
        {
            registrationId = _registrationId,
            stageId        = StageId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DelocalizedPeriodCountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Sending no header leaves the request anonymous — without this, a route that never checks
    /// cannot be told from one that always allows.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_reaches_none_of_the_routes()
    {
        using var client = _factory.CreateAnonymousClient();

        foreach (string route in new[]
                 {
                     "/api/stages/delocalize",
                     "/api/stages/delocalize/bulk",
                     "/api/stages/delocalize/bulk/preview",
                     "/api/stages/delocalize/cancel",
                 })
        {
            var response = await client.PostAsJsonAsync(route, new { });
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, route);
        }

        (await DelocalizedPeriodCountAsync()).Should().Be(0);
    }
}
