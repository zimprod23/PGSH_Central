using FluentAssertions;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Attendance.GetByPeriod;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Reading presence is wider than recording it: a student consults their own attendance from the
// student portal, which is not a privileged act. Gating the read behind the recording scope made
// every stage the portal displayed fire a 403 toast while the page itself rendered fine.
public class AttendanceReadScopeTests
{
    private const int ServiceId        = 1;
    private const int ForeignServiceId = 2;

    private static readonly Guid ChefIdentity       = Guid.NewGuid();
    private static readonly Guid OwnerIdentity      = Guid.NewGuid();
    private static readonly Guid ClassmateIdentity  = Guid.NewGuid();

    private static readonly DateOnly Start = new(2026, 6, 1);
    private static readonly DateOnly End   = new(2026, 6, 30);

    private sealed record Scenario(ServicePeriod Owned, ServicePeriod Foreign);

    /// <summary>Two students rotating in different services, and a chef who leads only the first.</summary>
    private static async Task<Scenario> SeedAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();
        var chef = db.SeedChef(ChefIdentity);
        var service = db.SeedService(ServiceId, "Cardiologie", chef);
        var foreignService = db.SeedService(ForeignServiceId, "Radiologie");
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");

        var ownerPeriod = SeedStudent("Sara", "Bennani", OwnerIdentity, service);
        var foreignPeriod = SeedStudent("Ali", "Amrani", ClassmateIdentity, foreignService);

        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), ServicePeriodId = ownerPeriod.Id,
            Date = Start, Status = AttendanceStatus.Present,
        });

        await db.SaveChangesAsync();
        return new Scenario(ownerPeriod, foreignPeriod);

        ServicePeriod SeedStudent(string first, string last, Guid identity, Service svc)
        {
            var registration = db.SeedRegistration(first, last, cohort.AcademicGroup);
            registration.Student.LinkIdentity(identity.ToString());
            var assignment = db.SeedAssignment(registration, cohort);
            return db.SeedPeriod(assignment, svc, Start, End);
        }
    }

    private static GetAttendanceQueryHandler Handler(ApplicationDbContext db, Guid identity, params string[] roles) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(identity, roles)));

    [Fact]
    public async Task A_student_can_read_the_attendance_of_their_own_rotation()
    {
        await using var db = TestHarness.NewContext("att-read-own");
        var s = await SeedAsync(db);

        var result = await Handler(db, OwnerIdentity, Roles.Student)
            .Handle(new GetAttendanceQuery(s.Owned.Id), default);

        result.IsSuccess.Should().BeTrue("consulting your own presence is not a privileged act");
        result.Value.Should().ContainSingle();
    }

    [Fact]
    public async Task A_student_cannot_read_a_classmates_attendance()
    {
        await using var db = TestHarness.NewContext("att-read-classmate");
        var s = await SeedAsync(db);

        var result = await Handler(db, OwnerIdentity, Roles.Student)
            .Handle(new GetAttendanceQuery(s.Foreign.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AttendanceNotAllowed);
    }

    [Fact]
    public async Task The_chef_of_the_service_still_reads_it()
    {
        await using var db = TestHarness.NewContext("att-read-chef");
        var s = await SeedAsync(db);

        var result = await Handler(db, ChefIdentity).Handle(new GetAttendanceQuery(s.Owned.Id), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_chef_still_cannot_read_a_service_he_does_not_lead()
    {
        await using var db = TestHarness.NewContext("att-read-chef-foreign");
        var s = await SeedAsync(db);

        var result = await Handler(db, ChefIdentity).Handle(new GetAttendanceQuery(s.Foreign.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AttendanceNotAllowed);
    }

    [Fact]
    public async Task An_administrative_user_still_reads_anything()
    {
        await using var db = TestHarness.NewContext("att-read-admin");
        var s = await SeedAsync(db);

        var result = await Handler(db, Guid.NewGuid(), Roles.Scolarite)
            .Handle(new GetAttendanceQuery(s.Foreign.Id), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_period_is_still_reported_as_not_found_rather_than_forbidden()
    {
        await using var db = TestHarness.NewContext("att-read-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await Handler(db, OwnerIdentity, Roles.Student)
            .Handle(new GetAttendanceQuery(missing), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotFound(missing),
            "a caller who owns nothing must not be told 'forbidden' for a row that does not exist");
    }

    [Fact]
    public async Task Recording_presence_stays_closed_to_the_student()
    {
        await using var db = TestHarness.NewContext("att-write-student");
        var s = await SeedAsync(db);
        var authorizer = new ExecutionAuthorizer(db, TestHarness.UserContext(OwnerIdentity, Roles.Student));

        var result = await authorizer.EnsureCanRecordAttendanceAsync(s.Owned.Id, default);

        result.IsFailure.Should().BeTrue("widening the read scope must not widen the write scope");
        result.Error.Should().Be(StageErrors.AttendanceNotAllowed);
    }
}
