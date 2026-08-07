using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Presence scoping: attendance may be recorded/read by a global admin, or by the chef OR staff of
// the period's service — nobody else. Mirrors the rule the chef evaluation path already enforces.
public class ExecutionAuthorizerAttendanceTests
{
    private const int ChefServiceId    = 1;   // the caller is chef here
    private const int StaffServiceId    = 2;  // the caller is only staff here
    private const int ForeignServiceId  = 3;  // the caller has no relation here

    private static readonly Guid KeycloakId = Guid.NewGuid();

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"attendance-scope-{Guid.NewGuid()}")
            .Options);

    private static IUserContext UserContext(params string[] roles)
    {
        var ctx = Substitute.For<IUserContext>();
        ctx.UserId.Returns(KeycloakId);
        ctx.IsInRole(Arg.Any<string>()).Returns(ci => roles.Contains((string)ci[0]));
        return ctx;
    }

    // A chef who leads ChefService, staffs StaffService, and is unrelated to ForeignService.
    // Returns the ids of one seeded period in each of the three services.
    private static async Task<(Guid chef, Guid staff, Guid foreign)> SeedAsync(ApplicationDbContext db)
    {
        var chef = new Employee { Id = Guid.NewGuid(), Email = "chef@x", Position = Position.ServiceChef };
        chef.LinkIdentity(KeycloakId.ToString());
        db.Users.Add(chef);

        var chefService    = new Service { Id = ChefServiceId,   Name = "Chef",    Description = "" };
        var staffService   = new Service { Id = StaffServiceId,  Name = "Staff",   Description = "" };
        var foreignService = new Service { Id = ForeignServiceId, Name = "Foreign", Description = "" };

        chefService.AddStaff(chef);
        chefService.AssignChef(chef).IsSuccess.Should().BeTrue();
        staffService.AddStaff(chef); // staff only — not chef

        db.Services.AddRange(chefService, staffService, foreignService);

        var chefPeriod    = NewPeriod(ChefServiceId);
        var staffPeriod   = NewPeriod(StaffServiceId);
        var foreignPeriod = NewPeriod(ForeignServiceId);
        db.ServicePeriods.AddRange(chefPeriod, staffPeriod, foreignPeriod);

        await db.SaveChangesAsync();
        return (chefPeriod.Id, staffPeriod.Id, foreignPeriod.Id);
    }

    private static ServicePeriod NewPeriod(int serviceId) => new()
    {
        Id = Guid.NewGuid(),
        InternshipAssignmentId = Guid.NewGuid(),
        ServiceId = serviceId,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 1, 31),
        IsStarted = true,
    };

    [Fact]
    public async Task Chef_can_act_on_a_period_in_his_own_service()
    {
        await using var db = NewContext();
        var (chefPeriod, _, _) = await SeedAsync(db);
        var authorizer = new ExecutionAuthorizer(db, UserContext());

        var result = await authorizer.EnsureCanRecordAttendanceAsync(chefPeriod, default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Staff_member_can_act_on_a_period_in_a_service_he_staffs()
    {
        await using var db = NewContext();
        var (_, staffPeriod, _) = await SeedAsync(db);
        var authorizer = new ExecutionAuthorizer(db, UserContext());

        var result = await authorizer.EnsureCanRecordAttendanceAsync(staffPeriod, default);

        result.IsSuccess.Should().BeTrue("a secretary staffing a service manages its presence like the chef");
    }

    [Fact]
    public async Task Non_member_is_refused_on_a_foreign_service()
    {
        await using var db = NewContext();
        var (_, _, foreignPeriod) = await SeedAsync(db);
        var authorizer = new ExecutionAuthorizer(db, UserContext());

        var result = await authorizer.EnsureCanRecordAttendanceAsync(foreignPeriod, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.AttendanceNotAllowed);
    }

    [Fact]
    public async Task Global_administrative_user_bypasses_the_service_scope()
    {
        await using var db = NewContext();
        var (_, _, foreignPeriod) = await SeedAsync(db);
        var authorizer = new ExecutionAuthorizer(db, UserContext(Roles.Scolarite));

        var result = await authorizer.EnsureCanRecordAttendanceAsync(foreignPeriod, default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_period_surfaces_not_found()
    {
        await using var db = NewContext();
        await SeedAsync(db);
        var authorizer = new ExecutionAuthorizer(db, UserContext());
        var missingId = Guid.NewGuid();

        var result = await authorizer.EnsureCanRecordAttendanceAsync(missingId, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotFound(missingId));
    }
}
