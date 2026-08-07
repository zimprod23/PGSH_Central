using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests;

/// <summary>
/// Shared scaffolding for application-layer tests: an isolated in-memory context, a stubbed identity,
/// and builders for the reference graph (year → level → stage → group → cohort → registration) that
/// nearly every handler needs before it has anything to act on.
/// </summary>
/// <remarks>
/// The in-memory provider does not enforce foreign keys, unique indexes or <c>OnDelete</c> behaviour,
/// and never checks that a query is translatable to SQL — constraint and translation defects are
/// invisible here and need the integration suite instead.
/// </remarks>
public static class TestHarness
{
    public const int CurrentYearId = 1;
    public const int LevelId       = 1;
    public const int StageId       = 1;
    public const int HospitalId    = 1;

    public static ApplicationDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{name}-{Guid.NewGuid()}")
            .Options);

    /// <summary>A caller identified by <paramref name="keycloakId"/> holding exactly <paramref name="roles"/>.</summary>
    public static IUserContext UserContext(Guid keycloakId, params string[] roles)
    {
        var ctx = Substitute.For<IUserContext>();
        ctx.UserId.Returns(keycloakId);
        ctx.IsInRole(Arg.Any<string>()).Returns(ci => roles.Contains((string)ci[0]));
        return ctx;
    }

    /// <summary>The current academic year plus the level and stage every cohort hangs off.</summary>
    public static Stage SeedCatalog(this ApplicationDbContext db, DateOnly? yearStart = null, DateOnly? yearEnd = null)
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = CurrentYearId, Label = "2025-2026", IsCurrent = true,
            StartDate = yearStart ?? new DateOnly(2025, 9, 1),
            EndDate   = yearEnd   ?? new DateOnly(2026, 8, 31),
        });

        var level = new Level { Id = LevelId, Label = "3ème année", Year = 3 };
        var stage = new Stage { Id = StageId, Name = "Cardiologie", LevelId = LevelId, Level = level, Coefficient = 2 };
        db.Levels.Add(level);
        db.Stages.Add(stage);
        return stage;
    }

    /// <summary>A hospital service, optionally led by <paramref name="chef"/> (who is added to its staff first).</summary>
    public static Service SeedService(this ApplicationDbContext db, int serviceId, string name, Employee? chef = null)
    {
        var hospital = db.Hospitals.Local.FirstOrDefault(h => h.Id == HospitalId);
        if (hospital is null)
        {
            hospital = new Hospital { Id = HospitalId, Name = "CHU Ibn Sina", City = "Rabat" };
            db.Hospitals.Add(hospital);
        }

        var service = new Service { Id = serviceId, Name = name, Description = "", Hospital = hospital, Capacity = 20 };
        if (chef is not null)
        {
            service.AddStaff(chef);
            service.AssignChef(chef);
        }

        db.Services.Add(service);
        return service;
    }

    public static Employee SeedChef(this ApplicationDbContext db, Guid keycloakId, string email = "chef@pgsh.ma")
    {
        var chef = new Employee { Id = Guid.NewGuid(), Email = email, Position = Position.ServiceChef };
        chef.LinkIdentity(keycloakId.ToString());
        db.Users.Add(chef);
        return chef;
    }

    /// <summary>A group and its cohort for <paramref name="stage"/> — the pair a rotation is planned against.</summary>
    public static Cohort SeedCohort(this ApplicationDbContext db, Stage stage, int groupId, string groupLabel)
    {
        var group = new AcademicGroup
        {
            Id = groupId, Label = groupLabel, GroupNumber = groupId, AcademicYearId = CurrentYearId,
        };
        var cohort = new Cohort
        {
            Id = groupId, Label = groupLabel, StageId = stage.Id, Stage = stage,
            AcademicGroupId = groupId, AcademicGroup = group,
        };
        db.AcademicGroups.Add(group);
        db.Cohorts.Add(cohort);
        return cohort;
    }

    /// <summary>A student with this year's registration, optionally attached to <paramref name="group"/>.</summary>
    public static Registration SeedRegistration(
        this ApplicationDbContext db, string firstName, string lastName, AcademicGroup? group = null,
        int academicYearId = CurrentYearId)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = firstName, LastName = lastName,
            Email = $"{firstName}.{lastName}@etu.ma".ToLowerInvariant(),
            CNE = $"CNE{Guid.NewGuid():N}"[..10], Appogee = $"AP{Guid.NewGuid():N}"[..8], BacYear = "2022",
        };
        var registration = new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = academicYearId, LevelId = LevelId,
            StudentId = student.Id, Student = student, AcademicGroupId = group?.Id,
        };
        db.Users.Add(student);
        db.Registrations.Add(registration);
        return registration;
    }

    /// <summary>An assignment on <paramref name="cohort"/> carrying its initial membership record.</summary>
    public static InternshipAssignment SeedAssignment(
        this ApplicationDbContext db, Registration registration, Cohort cohort, DateOnly? enrolledOn = null)
    {
        var assignment = new InternshipAssignment
        {
            Id = Guid.NewGuid(),
            RegistrationId = registration.Id, Registration = registration,
            CurrentCohortId = cohort.Id, Cohort = cohort,
        };
        assignment.MembershipHistory.Add(new CohortMembership
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id,
            CohortId = cohort.Id, StartDate = enrolledOn ?? new DateOnly(2025, 9, 1),
        });
        db.InternshipAssignments.Add(assignment);
        return assignment;
    }

    /// <summary>A rotation of <paramref name="assignment"/> in <paramref name="service"/>.</summary>
    public static ServicePeriod SeedPeriod(
        this ApplicationDbContext db, InternshipAssignment assignment, Service service,
        DateOnly start, DateOnly end, bool started = true, bool complete = false)
    {
        var period = new ServicePeriod
        {
            Id = Guid.NewGuid(),
            InternshipAssignmentId = assignment.Id, InternshipAssignment = assignment,
            ServiceId = service.Id, Service = service,
            StartDate = start, EndDate = end,
            IsStarted = started, IsComplete = complete,
        };
        assignment.ServicePeriods.Add(period);
        db.ServicePeriods.Add(period);
        return period;
    }

    /// <summary>A period of the stage's grid (P1, P2…) — the window every cohort is routed through.</summary>
    public static StageSlot SeedSlot(
        this ApplicationDbContext db, Stage stage, int slotId, int periodNumber, DateOnly start, DateOnly end)
    {
        var slot = new StageSlot
        {
            Id = slotId, StageId = stage.Id, PeriodNumber = periodNumber, StartDate = start, EndDate = end,
        };
        db.StageSlots.Add(slot);
        return slot;
    }

    /// <summary>One cell of the planning grid: this cohort spends this slot in this service.</summary>
    public static CohortSlotAssignment SeedSlotAssignment(
        this ApplicationDbContext db, int id, Cohort cohort, StageSlot slot, Service service)
    {
        var assignment = new CohortSlotAssignment
        {
            Id = id, CohortId = cohort.Id, Cohort = cohort,
            StageSlotId = slot.Id, StageSlot = slot,
            ServiceId = service.Id, Service = service,
        };
        db.CohortSlotAssignments.Add(assignment);
        return assignment;
    }

    public static StageObjective SeedObjective(
        this ApplicationDbContext db, Stage stage, int id, string label, int weight, bool mandatory = false)
    {
        var objective = new StageObjective
        {
            Id = id, StageId = stage.Id, Stage = stage,
            Label = label, Weight = weight, IsMandatory = mandatory,
        };
        db.StageObjectives.Add(objective);
        return objective;
    }
}
