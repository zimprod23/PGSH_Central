using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
/// The half of the nominative placement that is not the handler: the routes, the binding of a
/// selection sent as a body, and the refusal that must leave the store untouched.
/// </summary>
/// <remarks>
/// <para>⚠ <b>A guard ordered after the write returns the same failure and passes the handler
/// test.</b> The count-mismatch refusal is exactly that shape — it is the only thing standing between
/// a stale preview and a list applied to more students than anybody saw — so it is asserted here,
/// against the store, and paired with the request that must still succeed.</para>
///
/// <para>Both routes are POST because the selection is a body: whole rosters, named students and a
/// pasted list of identifiers do not fit a query string. The preview writes nothing.</para>
/// </remarks>
public class NominativePlacementEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;
    private const int StageId = 1;
    private const int SourceGroupId = 10;
    private const int TargetGroupId = 20;
    private const int ServiceId = 30;
    private const int HospitalId = 5;
    private const int CenterId = 2;

    private readonly ApiFactory _factory;

    public NominativePlacementEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        var year = new AcademicYear
        {
            Id = 1, Label = "2026-2027", IsCurrent = true,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31),
        };
        db.AcademicYears.Add(year);

        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Cardiologie", LevelId = LevelId,
            Coefficient = 1, DurationInDays = 15,
            RotationMode = StageRotationMode.SingleService,
        });

        db.Centers.Add(new Center { Id = CenterId, Name = "CHU Rabat", City = "Rabat" });
        db.Hospitals.Add(new Hospital
        {
            Id = HospitalId, Name = "GST Kénitra", City = "Kénitra", CenterId = CenterId,
        });
        db.Services.Add(new Service
        {
            Id = ServiceId, Name = "Médecine interne", Description = "",
            HospitalId = HospitalId, Capacity = 20,
        });

        foreach (int groupId in new[] { SourceGroupId, TargetGroupId })
        {
            db.AcademicGroups.Add(new AcademicGroup
            {
                Id = groupId, Label = $"Groupe {groupId}", GroupNumber = groupId,
                AcademicYearId = year.Id, LevelId = LevelId,
            });

            db.Cohorts.Add(new Cohort
            {
                Id = 100 + groupId, Label = $"Cardiologie · G{groupId}",
                StageId = StageId, AcademicGroupId = groupId,
            });
        }

        for (int i = 0; i < 2; i++)
        {
            var student = new Student
            {
                Id = Guid.NewGuid(), FirstName = $"Volontaire{i}", LastName = "Kénitra",
                Email = $"volontaire{i}@etu.ma", CNE = $"K{i}0000001", Appogee = $"AP{i}00001",
                BacYear = "2022", AcademicProgram = AcademicProgram.Medecine,
            };
            db.Users.Add(student);
            db.Registrations.Add(new Registration
            {
                Id = Guid.NewGuid(), AcademicYearId = year.Id, LevelId = LevelId,
                StudentId = student.Id, AcademicGroupId = SourceGroupId,
            });
        }
    });

    private static object Selection() => new
    {
        targetGroupId = TargetGroupId,
        targets = new { academicGroupIds = new[] { SourceGroupId } },
    };

    private static object Application(int confirmed) => new
    {
        targetGroupId = TargetGroupId,
        targets = new { academicGroupIds = new[] { SourceGroupId } },
        confirmedCount = confirmed,
        reason = "Volontaires Kénitra (GST) — formulaire du 12/09",
    };

    private static async Task<int> ApplicableCountAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("applicableCount").GetInt32();
    }

    [Fact]
    public async Task The_preview_reads_the_selection_from_the_body_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient(roles: Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/groups/assign/bulk/preview", Selection());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApplicableCountAsync(response)).Should().Be(2);

        (await _factory.QueryAsync(db =>
            db.Registrations.CountAsync(r => r.AcademicGroupId == TargetGroupId)))
            .Should().Be(0, "a preview that moved anybody would be an apply with a different name");
    }

    [Fact]
    public async Task The_apply_moves_the_confirmed_students()
    {
        using var client = _factory.CreateApiClient(roles: Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/groups/assign/bulk", Application(confirmed: 2));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _factory.QueryAsync(db =>
            db.Registrations.CountAsync(r => r.AcademicGroupId == TargetGroupId)))
            .Should().Be(2);
    }

    /// <summary>
    /// ⚠ The case this suite exists for. A count guard ordered after the write returns the same
    /// <c>Result.Failure</c> and passes the handler test; only the store tells the two apart.
    /// </summary>
    [Fact]
    public async Task A_stale_count_is_refused_and_nothing_is_written()
    {
        using var client = _factory.CreateApiClient(roles: Roles.Scolarite);

        var response = await client.PostAsJsonAsync("/api/groups/assign/bulk", Application(confirmed: 1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _factory.QueryAsync(db =>
            db.Registrations.CountAsync(r => r.AcademicGroupId == TargetGroupId)))
            .Should().Be(0);
        (await _factory.QueryAsync(db =>
            db.Registrations.CountAsync(r => r.AcademicGroupId == SourceGroupId)))
            .Should().Be(2);
    }

    [Fact]
    public async Task An_anonymous_caller_is_not_the_administration()
    {
        // ⚠ Sending no header leaves the request anonymous: a handler that always authenticates
        // cannot tell « allowed » from « never checked ».
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/groups/assign/bulk", Application(confirmed: 2));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await _factory.QueryAsync(db =>
            db.Registrations.CountAsync(r => r.AcademicGroupId == TargetGroupId)))
            .Should().Be(0);
    }

    /// <summary>
    /// ⚠ <b>The regression this test exists for was found by driving the real screen, not by the
    /// suite.</b> <c>AutoArrangeResult</c> is a second shape of <c>RotationArrangeResult</c> that the
    /// handler maps into, so extending the arranger's record left it behind: the two numbers were
    /// computed, carried, and then dropped at the API boundary — the response simply had no such
    /// fields, and no screen could have shown them whatever it did. Every handler test passed
    /// throughout, because they read the arranger's record directly.
    /// </summary>
    [Fact]
    public async Task The_arrange_response_carries_what_it_left_alone()
    {
        using var client = _factory.CreateApiClient(roles: Roles.Scolarite);

        var response = await client.PostAsJsonAsync(
            $"/api/stages/{StageId}/schedule/auto-arrange", new { academicYearId = 1 });

        // The stage has no créneaux, so the arrange refuses — which is fine: this asserts the
        // *shape* of the contract, and a refusal carries no shape at all. Give it a slot first.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await _factory.SeedAsync(db => db.StageSlots.Add(new StageSlot
        {
            Id = 1, StageId = StageId, AcademicYearId = 1, PeriodNumber = 1,
            StartDate = new DateOnly(2026, 9, 14), EndDate = new DateOnly(2026, 10, 13),
        }));

        var authorised = await client.PostAsJsonAsync(
            $"/api/stages/{StageId}/allowed-services", new { serviceId = ServiceId });
        authorised.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var arranged = await client.PostAsJsonAsync(
            $"/api/stages/{StageId}/schedule/auto-arrange", new { academicYearId = 1 });

        arranged.StatusCode.Should().Be(HttpStatusCode.OK);

        string payload = await arranged.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.TryGetProperty("pinnedCellsKept", out _).Should().BeTrue(
            "a count the arranger computes and the API drops can never reach a screen; payload was {0}", payload);
        doc.RootElement.TryGetProperty("reservedServices", out _).Should().BeTrue();
    }

    /// <summary>
    /// The placement mode, on the route that declares it. ⚠ Paired with the request that must
    /// succeed: a route 400ing on everything satisfies every refusal assertion and proves nothing.
    /// </summary>
    [Fact]
    public async Task A_service_is_reserved_through_its_authorisation_and_only_when_it_has_one()
    {
        using var client = _factory.CreateApiClient(roles: Roles.Scolarite);

        var unauthorised = await client.PutAsJsonAsync(
            $"/api/stages/{StageId}/allowed-services/{ServiceId}/placement-mode",
            new { placementMode = "Reserved" });

        unauthorised.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the mode is a property of an authorisation, so there has to be one to carry it");

        var authorised = await client.PostAsJsonAsync(
            $"/api/stages/{StageId}/allowed-services", new { serviceId = ServiceId });
        authorised.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reserved = await client.PutAsJsonAsync(
            $"/api/stages/{StageId}/allowed-services/{ServiceId}/placement-mode",
            new { placementMode = "Reserved" });

        reserved.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _factory.QueryAsync(db =>
            db.StageAllowedServices
                .SingleAsync(a => a.StageId == StageId && a.ServiceId == ServiceId)))
            .PlacementMode.Should().Be(ServicePlacementMode.Reserved);
    }
}
