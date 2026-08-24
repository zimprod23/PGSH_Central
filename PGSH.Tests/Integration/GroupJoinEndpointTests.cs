using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// <c>POST groups/assign-student</c> through the real pipeline.
///
/// <para>What lives outside the handler here is that the route is reachable at all by the person who
/// needs it, and that each refusal happens <b>before</b> the roster is written. The command binds
/// straight off the body, so a mistyped property name arrives as <c>0</c> or <c>Guid.Empty</c> and the
/// validator — not the guard the test believes it is exercising — produces the 400. Every refusal
/// below therefore asserts the store as well as the status.</para>
/// </summary>
public class GroupJoinEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int Level3 = 3;
    private const int Level4 = 4;

    private const int ThirdYearRoster = 10;
    private const int FourthYearRoster = 20;

    private const string NewcomerCne = "N13089613";
    private const string PlacedCne = "P13089614";

    private readonly ApiFactory _factory;

    public GroupJoinEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The ordinary September case: one promotion with its roster, a student who has just registered
    /// and belongs to none, and a classmate already placed. The fourth-year roster is there to be the
    /// wrong target — same year, same number, another promotion, which is exactly the shape the
    /// roster-identity index permits and the guard has to refuse.
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
            Id = Level3, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });
        db.Levels.Add(new Level
        {
            Id = Level4, Label = "Quatrième Année Médecine", Year = 4,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.AcademicGroups.Add(new AcademicGroup
        {
            Id = ThirdYearRoster, Label = "Groupe 1", GroupNumber = 1,
            AcademicYearId = YearId, LevelId = Level3,
        });
        db.AcademicGroups.Add(new AcademicGroup
        {
            Id = FourthYearRoster, Label = "Groupe 1", GroupNumber = 1,
            AcademicYearId = YearId, LevelId = Level4,
        });

        AddStudent(db, NewcomerCne, "Yassine", "Alaoui", groupId: null);
        AddStudent(db, PlacedCne, "Hind", "Chraibi", groupId: ThirdYearRoster);
    });

    private static void AddStudent(
        ApplicationDbContext db, string cne, string first, string last, int? groupId)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = last,
            Email = $"{cne.ToLowerInvariant()}@etu.test", CNE = cne, Appogee = $"AP{cne}",
            BacYear = "2022", AcademicProgram = AcademicProgram.Medecine,
        };

        db.Users.Add(student);
        db.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = Level3,
            StudentId = student.Id, Student = student,
            Status = RegistrationStatus.Active,
            AcademicGroupId = groupId,
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task<Guid> RegistrationIdAsync(string cne) => _factory.QueryAsync(db =>
        db.Registrations.Where(r => r.Student.CNE == cne).Select(r => r.Id).FirstAsync());

    private Task<int?> RosterOfAsync(string cne) => _factory.QueryAsync(db =>
        db.Registrations.AsNoTracking()
            .Where(r => r.Student.CNE == cne)
            .Select(r => r.AcademicGroupId)
            .FirstAsync());

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    private Task<HttpResponseMessage> JoinAsync(HttpClient client, Guid registrationId, int groupId) =>
        client.PostAsJsonAsync(
            "/api/groups/assign-student",
            new { registrationId, academicGroupId = groupId, reason = "Inscription tardive" });

    // ─── The tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// The control every refusal below needs: a route that 400s on everything would satisfy all of
    /// them and prove nothing. This is the request that must get through, and the assertion is the
    /// roster on the registration, not the 200.
    /// </summary>
    [Fact]
    public async Task A_registration_with_no_roster_joins_through_the_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await JoinAsync(client, await RegistrationIdAsync(NewcomerCne), ThirdYearRoster);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RosterOfAsync(NewcomerCne)).Should().Be(ThirdYearRoster);
    }

    /// <summary>
    /// A roster of another promotion, in the same year and carrying the same number — the pairing the
    /// index allows and nothing downstream can catch again, because every later check is keyed on the
    /// roster the registration claims.
    /// </summary>
    [Fact]
    public async Task A_roster_of_another_promotion_is_refused_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await JoinAsync(client, await RegistrationIdAsync(NewcomerCne), FourthYearRoster);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("AcademicGroups.TargetGroupInAnotherLevel");
        (await RosterOfAsync(NewcomerCne)).Should().BeNull("the refusal must precede the write");
    }

    /// <summary>
    /// Joining is not transferring. A student who is already somewhere has a rotation to carry across,
    /// which this path does not do — so it refuses rather than quietly rehoming him.
    /// </summary>
    [Fact]
    public async Task A_student_already_on_a_roster_is_sent_to_the_transfer_path()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await JoinAsync(client, await RegistrationIdAsync(PlacedCne), ThirdYearRoster);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("AcademicGroups.AlreadyInAGroup");
        (await RosterOfAsync(PlacedCne)).Should().Be(ThirdYearRoster);
    }

    /// <summary>
    /// ⚠ A handler that always authenticates cannot tell "allowed" from "not checked". The role is
    /// emitted as Keycloak's <c>realm_access</c> by <c>TestAuthHandler</c>, so
    /// <c>KeycloakRoleTransformer</c> is exercised rather than bypassed.
    /// </summary>
    [Fact]
    public async Task Only_the_administrative_side_may_place_a_student()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        var response = await JoinAsync(client, await RegistrationIdAsync(NewcomerCne), ThirdYearRoster);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RosterOfAsync(NewcomerCne)).Should().BeNull();
    }

    /// <summary>Sending no identity header at all leaves the request anonymous.</summary>
    [Fact]
    public async Task An_anonymous_caller_never_reaches_the_handler()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await JoinAsync(client, await RegistrationIdAsync(NewcomerCne), ThirdYearRoster);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RosterOfAsync(NewcomerCne)).Should().BeNull();
    }
}
