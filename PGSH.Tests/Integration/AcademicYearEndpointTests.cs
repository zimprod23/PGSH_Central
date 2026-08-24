using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// The academic-year routes through the real pipeline.
///
/// <para>What lives outside the handler here is the <b>shape of the request</b>. `PUT` merges a route
/// id with a body, and both dates arrive as JSON strings bound to <see cref="DateOnly"/> — a binding
/// that fails silently into <c>0001-01-01</c> rather than loudly, which would move a year onto a span
/// nobody asked for while the handler saw nothing wrong. And the delete's refusals are only worth
/// anything if the year is <em>still there</em> afterwards, which no handler test can see.</para>
/// </summary>
public class AcademicYearEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int CurrentYear = 1;
    private const int FutureYear = 2;
    private const int LevelId = 3;
    private const int StageId = 5;

    private readonly ApiFactory _factory;

    public AcademicYearEndpointTests(ApiFactory factory) => _factory = factory;

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
            Id = CurrentYear, Label = "2025-2026", IsCurrent = true,
            StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 8, 31),
        });
        db.AcademicYears.Add(new AcademicYear
        {
            Id = FutureYear, Label = "2026-2027", IsCurrent = false,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31),
        });

        var level = new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        };
        db.Levels.Add(level);
        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Chirurgie", LevelId = LevelId, Level = level, Coefficient = 1,
        });
    });

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task<AcademicYear?> YearAsync(int id) => _factory.QueryAsync(db =>
        db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id));

    private Task<int> CurrentCountAsync() => _factory.QueryAsync(db =>
        db.AcademicYears.AsNoTracking().CountAsync(y => y.IsCurrent));

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    // ─── Designating ──────────────────────────────────────────────────────────

    /// <summary>
    /// The control for every refusal below, and the assertion is the flag in the store — a 200 proves
    /// only that a route exists.
    /// </summary>
    [Fact]
    public async Task Designating_a_year_moves_the_flag_and_says_what_stood_down()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PostAsync($"/api/academic-years/{FutureYear}/current", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("previousLabel").GetString().Should().Be("2025-2026");

        (await YearAsync(FutureYear))!.IsCurrent.Should().BeTrue();
        (await CurrentCountAsync()).Should().Be(1, "the flag is a singleton the index enforces");
    }

    [Fact]
    public async Task Only_the_administrative_side_may_move_the_current_year()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        var response = await client.PostAsync($"/api/academic-years/{FutureYear}/current", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await YearAsync(CurrentYear))!.IsCurrent.Should().BeTrue("nothing may have moved");
    }

    [Fact]
    public async Task An_anonymous_caller_never_reaches_the_handler()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsync($"/api/academic-years/{FutureYear}/current", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await YearAsync(CurrentYear))!.IsCurrent.Should().BeTrue();
    }

    // ─── Deleting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_empty_year_is_removed_through_the_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.DeleteAsync($"/api/academic-years/{FutureYear}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await YearAsync(FutureYear)).Should().BeNull();
    }

    /// <summary>
    /// ⚠ The refusal is only worth something if the year survives it. On the live schema the
    /// registrations restrict and the rosters <em>cascade</em>, so a guard ordered after the delete
    /// would take the rosters with it and still return the same failure to a handler test.
    /// </summary>
    [Fact]
    public async Task A_year_holding_registrations_is_refused_and_still_there_afterwards()
    {
        await _factory.SeedAsync(db => SeedRegistration(db, FutureYear));
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.DeleteAsync($"/api/academic-years/{FutureYear}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("AcademicYears.StillInUse");
        (await YearAsync(FutureYear)).Should().NotBeNull();
    }

    [Fact]
    public async Task The_current_year_survives_a_delete()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.DeleteAsync($"/api/academic-years/{CurrentYear}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("AcademicYears.CannotDeleteCurrent");
        (await YearAsync(CurrentYear)).Should().NotBeNull();
    }

    // ─── Updating ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Both dates travel as JSON strings and are bound to <see cref="DateOnly"/> from the body,
    /// while the id comes off the route. The stored span is the assertion: a binding that failed would
    /// write <c>0001-01-01</c> and answer 200.
    /// </summary>
    [Fact]
    public async Task A_span_sent_as_strings_lands_as_the_span()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PutAsJsonAsync(
            $"/api/academic-years/{FutureYear}",
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-09-30" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = (await YearAsync(FutureYear))!;
        stored.StartDate.Should().Be(new DateOnly(2026, 10, 1));
        stored.EndDate.Should().Be(new DateOnly(2027, 9, 30));
    }

    [Fact]
    public async Task A_year_moved_onto_another_years_days_is_refused_and_keeps_its_span()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PutAsJsonAsync(
            $"/api/academic-years/{FutureYear}",
            new { label = "2026-2027", startDate = "2026-06-01", endDate = "2027-05-31" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("AcademicYears.OverlapsAnotherYear");
        (await YearAsync(FutureYear))!.StartDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    /// <summary>
    /// Narrowing a year leaves the périodes laid on it where they were. Reported, not refused — and
    /// the count has to survive the round trip, since it is what the confirmation names.
    /// </summary>
    [Fact]
    public async Task Narrowing_a_year_reports_the_periodes_left_outside_it()
    {
        await _factory.SeedAsync(db => db.StageSlots.Add(new StageSlot
        {
            Id = 77, StageId = StageId, AcademicYearId = FutureYear, PeriodNumber = 1,
            StartDate = new DateOnly(2027, 7, 1), EndDate = new DateOnly(2027, 7, 31),
        }));

        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.PutAsJsonAsync(
            $"/api/academic-years/{FutureYear}",
            new { label = "2026-2027", startDate = "2026-09-01", endDate = "2027-06-30" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("slotsOutsideSpan").GetInt32().Should().Be(1);
    }

    /// <summary>A malformed id is routing's answer, never the handler's, and never a 500.</summary>
    [Fact]
    public async Task A_malformed_year_id_is_not_a_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await client.DeleteAsync("/api/academic-years/not-a-number");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void SeedRegistration(ApplicationDbContext db, int academicYearId)
    {
        var student = new PGSH.Domain.Students.Student
        {
            Id = Guid.NewGuid(), FirstName = "Yassine", LastName = "Alaoui",
            Email = "yassine.alaoui@etu.test", CNE = "Y13089613", Appogee = "AP13089613",
            BacYear = "2022", AcademicProgram = AcademicProgram.Medecine,
        };

        db.Users.Add(student);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = academicYearId, LevelId = LevelId,
            StudentId = student.Id, Student = student, Status = RegistrationStatus.Active,
        });
    }
}
