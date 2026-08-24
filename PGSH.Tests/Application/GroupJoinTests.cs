using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.Join;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// A student registered after the groups were cut and the schedule published — the ordinary September
/// case, and the one the transfer path silently did nothing about: every step of a transfer filters on
/// assignments the newcomer does not have, so he landed on the roster with no cohorte and no période,
/// looking exactly like a student who had been planned.
/// </summary>
public class GroupJoinTests
{
    private static AssignStudentToGroupCommandHandler Handler(ApplicationDbContext db) =>
        new(db, new StudentAffectationService(db), new LateArrivalScheduler(db), db.AdminAuthorizer());

    /// <summary>
    /// One roster taking two stages: one whose window closed last month, one still to come.
    /// </summary>
    private static (Registration Newcomer, Cohort Past, Cohort Future) SeedPublishedRoster(
        ApplicationDbContext db, DateOnly today)
    {
        var over = db.SeedCatalog();
        var ahead = db.SeedStage(stageId: 2, "Pédiatrie");
        var service = db.SeedService(2, "Cardiologie");

        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        var past = db.SeedCohortFor(over, group, cohortId: 30);
        var future = db.SeedCohortFor(ahead, group, cohortId: 31);

        var closedSlot = db.SeedSlot(over, slotId: 1, periodNumber: 1,
            today.AddDays(-60), today.AddDays(-30));
        var openSlot = db.SeedSlot(ahead, slotId: 2, periodNumber: 1,
            today.AddDays(30), today.AddDays(60));

        db.SeedSlotAssignment(1, past, closedSlot, service);
        db.SeedSlotAssignment(2, future, openSlot, service);

        var newcomer = db.SeedRegistration("Sara", "Bennani");
        return (newcomer, past, future);
    }

    [Fact]
    public async Task Joining_gives_the_cohorts_of_the_roster_and_only_the_rotations_still_ahead()
    {
        await using var db = TestHarness.NewContext(nameof(Joining_gives_the_cohorts_of_the_roster_and_only_the_rotations_still_ahead));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (newcomer, past, future) = SeedPublishedRoster(db, today);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssignStudentToGroupCommand(newcomer.Id, 10), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CohortsJoined.Should().Be(2);
        result.Value.PeriodsCreated.Should().Be(1);
        result.Value.StagesAlreadyOver.Should().Be(1);

        var assignments = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .Where(a => a.RegistrationId == newcomer.Id)
            .ToListAsync();

        // He owes the stage that is over — it shows on his dossier as unserved — but nothing claims he
        // stood in a service on days he was not enrolled.
        assignments.Single(a => a.CurrentCohortId == past.Id).ServicePeriods.Should().BeEmpty();
        assignments.Single(a => a.CurrentCohortId == future.Id).ServicePeriods.Should().ContainSingle();

        (await db.Registrations.SingleAsync(r => r.Id == newcomer.Id))
            .AcademicGroupId.Should().Be(10);
    }

    [Fact]
    public async Task A_rotation_the_roster_has_already_begun_is_created_started()
    {
        await using var db = TestHarness.NewContext(nameof(A_rotation_the_roster_has_already_begun_is_created_started));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var stage = db.SeedCatalog();
        var service = db.SeedService(2, "Cardiologie");
        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        var cohort = db.SeedCohortFor(stage, group, cohortId: 30);
        var slot = db.SeedSlot(stage, slotId: 1, periodNumber: 1, today.AddDays(-5), today.AddDays(25));
        var cell = db.SeedSlotAssignment(1, cohort, slot, service);

        // A colleague already in the running rotation: the roster has started this cell.
        var colleague = db.SeedRegistration("Ali", "Amrani", group);
        var running = db.SeedAssignment(colleague, cohort);
        var period = db.SeedPeriod(running, service, slot.StartDate, slot.EndDate);
        period.CohortSlotAssignmentId = cell.Id;

        var newcomer = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssignStudentToGroupCommand(newcomer.Id, 10), default);

        result.IsSuccess.Should().BeTrue();

        var joined = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .SingleAsync(a => a.RegistrationId == newcomer.Id);

        var created = joined.ServicePeriods.Single();
        created.IsStarted.Should().BeTrue();

        // Never back-dated to a window that opened before he was enrolled.
        created.StartDate.Should().Be(today);
    }

    [Fact]
    public async Task A_student_who_already_has_a_group_is_refused_and_nothing_moves()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_who_already_has_a_group_is_refused_and_nothing_moves));
        var stage = db.SeedCatalog();
        var origin = db.SeedGroup(groupId: 10, groupNumber: 10);
        var target = db.SeedGroup(groupId: 11, groupNumber: 11);
        db.SeedCohortFor(stage, target, cohortId: 31);

        var registration = db.SeedRegistration("Sara", "Bennani", origin);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssignStudentToGroupCommand(registration.Id, 11), default);

        // Moving him is a transfer: his running rotation has to be interrupted and rehomed, and this
        // command does none of that.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.AlreadyInAGroup");

        (await db.Registrations.SingleAsync(r => r.Id == registration.Id))
            .AcademicGroupId.Should().Be(10);
        (await db.InternshipAssignments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_roster_of_another_promotion_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(A_roster_of_another_promotion_is_refused));
        db.SeedCatalog();
        db.SeedLevel(levelId: 4, "4ème année", year: 4);

        var otherPromotion = new AcademicGroup
        {
            Id = 12, Label = "G12", GroupNumber = 12,
            AcademicYearId = TestHarness.CurrentYearId, LevelId = 4,
        };
        db.AcademicGroups.Add(otherPromotion);

        var registration = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssignStudentToGroupCommand(registration.Id, 12), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.TargetGroupInAnotherLevel");
        (await db.Registrations.SingleAsync(r => r.Id == registration.Id))
            .AcademicGroupId.Should().BeNull();
    }

    [Fact]
    public async Task A_student_whose_year_is_closed_cannot_be_planned_into_a_roster()
    {
        await using var db = TestHarness.NewContext(nameof(A_student_whose_year_is_closed_cannot_be_planned_into_a_roster));
        var stage = db.SeedCatalog();
        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        db.SeedCohortFor(stage, group, cohortId: 30);

        var registration = db.SeedRegistration("Sara", "Bennani");
        registration.RecordYearOutcome(
            RegistrationStatus.Withdrawn, RegistrationOutcomeSource.Declared, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssignStudentToGroupCommand(registration.Id, 10), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AcademicGroups.CursusEndedCannotJoin");
        (await db.InternshipAssignments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Joining_a_roster_that_has_no_cohort_yet_is_a_plain_assignment()
    {
        await using var db = TestHarness.NewContext(nameof(Joining_a_roster_that_has_no_cohort_yet_is_a_plain_assignment));
        db.SeedCatalog();
        db.SeedGroup(groupId: 10, groupNumber: 10);
        var registration = db.SeedRegistration("Sara", "Bennani");
        await db.SaveChangesAsync();

        // The normal case before any planning is authored: the roster exists, nothing hangs off it.
        var result = await Handler(db).Handle(new AssignStudentToGroupCommand(registration.Id, 10), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CohortsJoined.Should().Be(0);
        (await db.Registrations.SingleAsync(r => r.Id == registration.Id)).AcademicGroupId.Should().Be(10);
    }

    [Fact]
    public async Task Joining_twice_creates_no_second_set_of_assignments()
    {
        await using var db = TestHarness.NewContext(nameof(Joining_twice_creates_no_second_set_of_assignments));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (newcomer, _, _) = SeedPublishedRoster(db, today);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new AssignStudentToGroupCommand(newcomer.Id, 10), default);
        var second = await Handler(db).Handle(new AssignStudentToGroupCommand(newcomer.Id, 10), default);

        // The second call is refused as "already in a group", which is the guard doing its job — but
        // the count is what matters: a duplicated affectation is a student counted twice in a service.
        second.IsFailure.Should().BeTrue();
        (await db.InternshipAssignments.CountAsync(a => a.RegistrationId == newcomer.Id)).Should().Be(2);
    }
}
