using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Attendance.GetByPeriod;
using PGSH.Application.Stages.Attendance.Record;
using PGSH.Domain.Employees;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Presence is service-scoped, and more broadly than evaluation: the chef of the period's service and
// any staff member attached to it may record it (a secretary keeps the register), plus administrative
// users. Recording the same day twice corrects the entry rather than creating a duplicate.
public class AttendanceHandlerTests
{
    private const int ChefServiceId    = 1;
    private const int StaffServiceId   = 2;
    private const int ForeignServiceId = 3;

    private static readonly Guid CallerIdentity = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End   = new(2026, 3, 31);
    private static readonly DateOnly Day   = new(2026, 3, 10);

    private sealed record Scenario(ServicePeriod Chef, ServicePeriod Staff, ServicePeriod Foreign);

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();

        var caller = new Employee { Id = Guid.NewGuid(), Email = "sec@pgsh.ma", Position = Position.ServiceChef };
        caller.LinkIdentity(CallerIdentity.ToString());
        db.Users.Add(caller);

        var chefService = db.SeedService(ChefServiceId, "Cardiologie", caller);
        var staffService = db.SeedService(StaffServiceId, "Réanimation");
        staffService.AddStaff(caller);                       // staff only — not chef
        var foreignService = db.SeedService(ForeignServiceId, "Pédiatrie");

        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Yasmine", "Idrissi", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);

        var scenario = new Scenario(
            db.SeedPeriod(assignment, chefService, Start, End),
            db.SeedPeriod(assignment, staffService, Start, End),
            db.SeedPeriod(assignment, foreignService, Start, End));

        await db.SaveChangesAsync();
        return scenario;
    }

    private static RecordAttendanceCommandHandler RecordHandler(ApplicationDbContext db, params string[] roles) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(CallerIdentity, roles)));

    private static GetAttendanceQueryHandler ReadHandler(ApplicationDbContext db, params string[] roles) =>
        new(db, new ExecutionAuthorizer(db, TestHarness.UserContext(CallerIdentity, roles)));

    [Fact]
    public async Task The_chef_records_presence_in_his_own_service()
    {
        await using var db = TestHarness.NewContext("att-chef");
        var s = await SeedAsync(db);

        var result = await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Chef.Id, Day, AttendanceStatus.Present), default);

        result.IsSuccess.Should().BeTrue();
        var saved = await db.AttendanceRecords.SingleAsync(a => a.ServicePeriodId == s.Chef.Id);
        saved.Status.Should().Be(AttendanceStatus.Present);
        saved.Date.Should().Be(Day);
    }

    [Fact]
    public async Task A_staff_member_records_presence_like_the_chef()
    {
        await using var db = TestHarness.NewContext("att-staff");
        var s = await SeedAsync(db);

        var result = await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Staff.Id, Day, AttendanceStatus.Late), default);

        result.IsSuccess.Should().BeTrue("a secretary attached to a service keeps its register");
    }

    [Fact]
    public async Task Nobody_records_presence_for_a_service_they_are_unrelated_to()
    {
        await using var db = TestHarness.NewContext("att-foreign");
        var s = await SeedAsync(db);

        var result = await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Foreign.Id, Day, AttendanceStatus.Present), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AttendanceNotAllowed);
        (await db.AttendanceRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_administrative_user_records_presence_anywhere()
    {
        await using var db = TestHarness.NewContext("att-admin");
        var s = await SeedAsync(db);

        var result = await RecordHandler(db, Roles.Scolarite).Handle(
            new RecordAttendanceCommand(s.Foreign.Id, Day, AttendanceStatus.Absent), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Recording_the_same_day_twice_corrects_the_entry_instead_of_duplicating_it()
    {
        await using var db = TestHarness.NewContext("att-correct");
        var s = await SeedAsync(db);
        var first = await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Chef.Id, Day, AttendanceStatus.Absent), default);

        var second = await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Chef.Id, Day, AttendanceStatus.JustifiedAbsent), default);

        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(first.Value, "the same record is amended");
        var records = await db.AttendanceRecords.Where(a => a.ServicePeriodId == s.Chef.Id).ToListAsync();
        records.Should().ContainSingle().Which.Status.Should().Be(AttendanceStatus.JustifiedAbsent);
    }

    [Fact]
    public async Task Different_days_are_recorded_separately()
    {
        await using var db = TestHarness.NewContext("att-days");
        var s = await SeedAsync(db);

        await RecordHandler(db).Handle(new RecordAttendanceCommand(s.Chef.Id, Day, AttendanceStatus.Present), default);
        await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Chef.Id, Day.AddDays(1), AttendanceStatus.Absent), default);

        (await db.AttendanceRecords.CountAsync(a => a.ServicePeriodId == s.Chef.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Recording_against_an_unknown_period_is_not_found()
    {
        await using var db = TestHarness.NewContext("att-missing");
        await SeedAsync(db);
        var missing = Guid.NewGuid();

        var result = await RecordHandler(db, Roles.Scolarite).Handle(
            new RecordAttendanceCommand(missing, Day, AttendanceStatus.Present), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotFound(missing));
    }

    [Fact]
    public async Task The_register_reads_back_in_date_order()
    {
        await using var db = TestHarness.NewContext("att-read");
        var s = await SeedAsync(db);
        await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Chef.Id, Day.AddDays(2), AttendanceStatus.Absent), default);
        await RecordHandler(db).Handle(
            new RecordAttendanceCommand(s.Chef.Id, Day, AttendanceStatus.Present), default);

        var result = await ReadHandler(db).Handle(new GetAttendanceQuery(s.Chef.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(r => r.Date).Should().BeInAscendingOrder();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_register_of_a_foreign_service_cannot_be_read()
    {
        await using var db = TestHarness.NewContext("att-read-foreign");
        var s = await SeedAsync(db);

        var result = await ReadHandler(db).Handle(new GetAttendanceQuery(s.Foreign.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AttendanceNotAllowed);
    }
}
