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
/// <c>POST groups/change-student-group</c> and <c>POST groups/swap-students</c> through the real
/// pipeline.
///
/// <para>Two things live outside the handler and only this suite can see them. The first is the usual
/// one: each refusal has to happen <b>before</b> the roster pointer is written, and a handler test
/// cannot tell a pre-check from a post-check. The second is specific to this act — it is the one act
/// in the application whose whole purpose is to leave <b>no</b> trace on the student's file, so the
/// question « et le registre, lui, garde-t-il quelque chose ? » has to be asked against the real
/// chain: the audit row is staged by <c>AuditLogPipelineBehavior</c> before the handler runs and
/// committed by the handler's own <c>SaveChanges</c>, so nothing short of the pipeline answers it.</para>
/// </summary>
public class GroupChangeEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int Level3 = 3;
    private const int Level4 = 4;
    private const int StageId = 1;

    private const int RosterA = 10;
    private const int RosterB = 20;
    private const int OtherPromotionRoster = 30;

    private const int CohortA = 101;
    private const int CohortB = 102;

    private const string SaraCne = "S13089613";
    private const string AliCne = "A13089614";

    private readonly ApiFactory _factory;

    public GroupChangeEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Two rosters of one promotion, each running the stage, each holding a student — plus a roster of
    /// the fourth year carrying the same number, which is the pairing the roster-identity index permits
    /// and that nothing downstream catches again.
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

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Cardiologie", LevelId = Level3, Coefficient = 2,
        });

        foreach (var (id, level) in new[] { (RosterA, Level3), (RosterB, Level3), (OtherPromotionRoster, Level4) })
            db.AcademicGroups.Add(new AcademicGroup
            {
                Id = id, Label = $"Groupe {id / 10}", GroupNumber = id / 10,
                AcademicYearId = YearId, LevelId = level,
            });

        db.Cohorts.Add(new Cohort
        {
            Id = CohortA, Label = "Cardio · A", StageId = StageId, AcademicGroupId = RosterA,
        });
        db.Cohorts.Add(new Cohort
        {
            Id = CohortB, Label = "Cardio · B", StageId = StageId, AcademicGroupId = RosterB,
        });

        AddStudent(db, SaraCne, "Sara", "Bennani", RosterA, CohortA);
        AddStudent(db, AliCne, "Ali", "Amrani", RosterB, CohortB);
    });

    private static void AddStudent(
        ApplicationDbContext db, string cne, string first, string last, int rosterId, int cohortId)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = last,
            Email = $"{cne.ToLowerInvariant()}@etu.test", CNE = cne, Appogee = $"AP{cne}",
            BacYear = "2022", AcademicProgram = AcademicProgram.Medecine,
        };

        var registration = new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = YearId, LevelId = Level3,
            StudentId = student.Id, Student = student,
            Status = RegistrationStatus.Active,
            AcademicGroupId = rosterId,
        };

        var assignmentId = Guid.NewGuid();
        var assignment = new InternshipAssignment
        {
            Id = assignmentId, RegistrationId = registration.Id, Registration = registration,
            CurrentCohortId = cohortId,
        };
        assignment.MembershipHistory.Add(new CohortMembership
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = assignmentId,
            CohortId = cohortId, StartDate = new DateOnly(2025, 9, 1),
        });

        db.Users.Add(student);
        db.Registrations.Add(registration);
        db.InternshipAssignments.Add(assignment);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Task<Guid> RegistrationIdAsync(string cne) => _factory.QueryAsync(db =>
        db.Registrations.Where(r => r.Student.CNE == cne).Select(r => r.Id).FirstAsync());

    private Task<int?> RosterOfAsync(string cne) => _factory.QueryAsync(db =>
        db.Registrations.AsNoTracking()
            .Where(r => r.Student.CNE == cne)
            .Select(r => r.AcademicGroupId)
            .FirstAsync());

    private Task<int> CohortOfAsync(string cne) => _factory.QueryAsync(db =>
        db.InternshipAssignments.AsNoTracking()
            .Where(a => a.Registration.Student.CNE == cne)
            .Select(a => a.CurrentCohortId)
            .FirstAsync());

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
    }

    private Task<HttpResponseMessage> ChangeAsync(HttpClient client, Guid registrationId, int groupId) =>
        client.PostAsJsonAsync(
            "/api/groups/change-student-group",
            new { registrationId, targetGroupId = groupId });

    private Task<HttpResponseMessage> SwapAsync(HttpClient client, Guid first, Guid second) =>
        client.PostAsJsonAsync(
            "/api/groups/swap-students",
            new { firstRegistrationId = first, secondRegistrationId = second });

    // ─── The tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// The control every refusal below needs: a route that 400s on everything satisfies all of them
    /// and proves nothing. The assertion is the roster and the cohorte, not the 200.
    /// </summary>
    [Fact]
    public async Task A_student_changes_roster_through_the_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await ChangeAsync(client, await RegistrationIdAsync(SaraCne), RosterB);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RosterOfAsync(SaraCne)).Should().Be(RosterB);
        (await CohortOfAsync(SaraCne)).Should().Be(CohortB, "the affectation follows the roster");
    }

    /// <summary>
    /// ⚠ <b>Silent toward the dossier, never toward the register.</b> The act writes no
    /// <c>HistoryType.GroupTransfer</c> row and rewrites the membership in place, so the roster the
    /// student came from survives in exactly one place — the journal. Asserting both halves together is
    /// the point: « aucune trace » would otherwise be indistinguishable from « acte non enregistré ».
    /// </summary>
    [Fact]
    public async Task The_dossier_keeps_nothing_and_the_register_keeps_everything()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);
        var registrationId = await RegistrationIdAsync(SaraCne);

        (await ChangeAsync(client, registrationId, RosterB)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var histories = await _factory.QueryAsync(db => db.Histories.AsNoTracking().CountAsync());
        histories.Should().Be(0, "a changement de groupe writes nothing on the student's file");

        var assignmentId = await _factory.QueryAsync(db => db.InternshipAssignments.AsNoTracking()
            .Where(a => a.RegistrationId == registrationId)
            .Select(a => a.Id)
            .FirstAsync());

        var memberships = await _factory.QueryAsync(db => db.CohortMembership.AsNoTracking()
            .Where(m => m.InternshipAssignmentId == assignmentId)
            .ToListAsync());

        memberships.Should().ContainSingle("the open row is rewritten, never closed and replaced");
        memberships[0].CohortId.Should().Be(CohortB);
        memberships[0].EndDate.Should().BeNull();

        var entry = await _factory.QueryAsync(db => db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "STUDENT_GROUP_CHANGED")
            .FirstOrDefaultAsync());

        entry.Should().NotBeNull("the act cannot be undone, so the register is the only record of it");
        entry!.EntityId.Should().Be(registrationId.ToString());
        entry.Metadata.Should().Contain(RosterB.ToString());
    }

    /// <summary>
    /// A roster of another promotion: the FK is satisfiable and every later guard is keyed on the
    /// roster the registration claims, so it is caught here or never.
    /// </summary>
    [Fact]
    public async Task A_roster_of_another_promotion_is_refused_and_writes_nothing()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await ChangeAsync(client, await RegistrationIdAsync(SaraCne), OtherPromotionRoster);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TitleAsync(response)).Should().Be("AcademicGroups.TargetGroupInAnotherLevel");
        (await RosterOfAsync(SaraCne)).Should().Be(RosterA, "the refusal must precede the write");
        (await CohortOfAsync(SaraCne)).Should().Be(CohortA);
    }

    /// <summary>
    /// ⚠ A refused act writes no journal entry either — the row is staged before the handler and only
    /// its <c>SaveChanges</c> commits it. A register that listed attempts would drown what took place.
    /// </summary>
    [Fact]
    public async Task A_refused_change_leaves_no_entry_in_the_register()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        await ChangeAsync(client, await RegistrationIdAsync(SaraCne), OtherPromotionRoster);

        var entries = await _factory.QueryAsync(db => db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "STUDENT_GROUP_CHANGED")
            .CountAsync());

        entries.Should().Be(0);
    }

    [Fact]
    public async Task An_echange_exchanges_the_two_rosters_through_the_route()
    {
        using var client = _factory.CreateApiClient(null, Roles.Scolarite);

        var response = await SwapAsync(
            client, await RegistrationIdAsync(SaraCne), await RegistrationIdAsync(AliCne));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await RosterOfAsync(SaraCne)).Should().Be(RosterB);
        (await RosterOfAsync(AliCne)).Should().Be(RosterA);
        (await CohortOfAsync(SaraCne)).Should().Be(CohortB);
        (await CohortOfAsync(AliCne)).Should().Be(CohortA);
    }

    /// <summary>
    /// ⚠ A handler that always authenticates cannot tell "allowed" from "not checked". The role is
    /// emitted as Keycloak's <c>realm_access</c> by <c>TestAuthHandler</c>, so
    /// <c>KeycloakRoleTransformer</c> is exercised rather than bypassed.
    /// </summary>
    [Fact]
    public async Task Only_the_administrative_side_may_correct_a_roster()
    {
        using var client = _factory.CreateApiClient(null, Roles.Professor);

        var response = await ChangeAsync(client, await RegistrationIdAsync(SaraCne), RosterB);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RosterOfAsync(SaraCne)).Should().Be(RosterA);
    }

    /// <summary>Sending no identity header at all leaves the request anonymous.</summary>
    [Fact]
    public async Task An_anonymous_caller_never_reaches_the_handler()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await SwapAsync(
            client, await RegistrationIdAsync(SaraCne), await RegistrationIdAsync(AliCne));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RosterOfAsync(SaraCne)).Should().Be(RosterA);
    }
}
