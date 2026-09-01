using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// Verifies the plain (toggle-off) transfer behaviour the user asked for: a student transferred while
// his stage is en cours joins the new group's period at the same status as his new colleagues so the
// NEW chef can evaluate, while the in-flight rotation at the origin service is cut to read-only
// history so the OLD chef only sees "he was here and left".
public class MidStageTransferReschedulerTests
{
    private const int OriginCohortId  = 1;
    private const int TargetCohortId  = 2;
    private const int OriginServiceId = 10;
    private const int TargetServiceId = 20;
    private const int StageSlotId     = 100;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"reschedule-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    // One StageSlot (period 1) shared by both cohorts; the target group is already running it (a
    // colleague has a started period on the target slot), so a transferred student can land there.
    private static async Task SeedScheduleAsync(ApplicationDbContext db)
    {
        db.StageSlots.Add(new StageSlot
        {
            Id = StageSlotId, StageId = 1, PeriodNumber = 1,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
        });

        db.CohortSlotAssignments.AddRange(
            new CohortSlotAssignment { Id = 1, CohortId = OriginCohortId, StageSlotId = StageSlotId, ServiceId = OriginServiceId },
            new CohortSlotAssignment { Id = 2, CohortId = TargetCohortId, StageSlotId = StageSlotId, ServiceId = TargetServiceId });

        // A colleague already rotating in the target group's period 1 → the slot counts as "started".
        db.ServicePeriods.Add(new ServicePeriod
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = Guid.NewGuid(),
            ServiceId = TargetServiceId, CohortSlotAssignmentId = 2,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            IsStarted = true,
        });

        await db.SaveChangesAsync();
    }

    private static InternshipAssignment InFlightAssignment(ApplicationDbContext db)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = TargetCohortId };
        var inFlight = new ServicePeriod
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id,
            ServiceId = OriginServiceId, CohortSlotAssignmentId = 1,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            IsStarted = true,
            CohortSlotAssignment = db.CohortSlotAssignments.Local.Single(x => x.Id == 1),
        };
        assignment.ServicePeriods.Add(inFlight);
        return assignment;
    }

    [Fact]
    public async Task Materialize_interrupts_the_in_flight_period_and_lands_the_student_on_the_target_service()
    {
        await using var db = NewContext();
        await SeedScheduleAsync(db);
        var assignment = InFlightAssignment(db);
        var transferDate = new DateOnly(2026, 1, 10);

        var result = await new MidStageTransferRescheduler(db)
            .MaterializeAtTargetAsync(assignment, TargetCohortId, transferDate, default);

        result.IsSuccess.Should().BeTrue();

        var oldPeriod = assignment.ServicePeriods.Single(p => p.ServiceId == OriginServiceId);
        oldPeriod.IsInterrupted.Should().BeTrue("the origin rotation becomes read-only history");
        oldPeriod.EndDate.Should().Be(transferDate, "it is cut at the transfer date");

        var newPeriod = assignment.ServicePeriods.Single(p => p.ServiceId == TargetServiceId);
        newPeriod.IsStarted.Should().BeTrue("the student joins his new colleagues' running rotation");
        newPeriod.IsInterrupted.Should().BeFalse();
        newPeriod.CohortSlotAssignmentId.Should().Be(2, "it is materialised against the target group's slot");
    }

    [Fact]
    public async Task Landing_period_is_closed_when_the_target_group_already_closed_that_period()
    {
        await using var db = NewContext();
        await SeedScheduleAsync(db);
        // The target group's colleague rotation for this period is already clôturée (à évaluer).
        foreach (var p in db.ServicePeriods.Where(p => p.CohortSlotAssignmentId == 2).ToList())
            p.IsComplete = true;
        await db.SaveChangesAsync();

        var assignment = InFlightAssignment(db);

        var result = await new MidStageTransferRescheduler(db)
            .MaterializeAtTargetAsync(assignment, TargetCohortId, new DateOnly(2026, 1, 10), default);

        result.IsSuccess.Should().BeTrue();
        var newPeriod = assignment.ServicePeriods.Single(p => p.ServiceId == TargetServiceId);
        newPeriod.IsStarted.Should().BeTrue();
        newPeriod.IsComplete.Should().BeTrue("the student joins a group whose period is already clôturé, so he is immediately evaluable");
    }

    [Fact]
    public async Task Materialize_keeps_the_in_flight_period_when_the_target_group_has_not_started_that_period()
    {
        await using var db = NewContext();
        await SeedScheduleAsync(db);
        // Remove the colleague's started period so the target slot is NOT running yet.
        db.ServicePeriods.RemoveRange(db.ServicePeriods.Where(p => p.CohortSlotAssignmentId == 2));
        await db.SaveChangesAsync();

        var assignment = InFlightAssignment(db);

        var result = await new MidStageTransferRescheduler(db)
            .MaterializeAtTargetAsync(assignment, TargetCohortId, new DateOnly(2026, 1, 10), default);

        result.IsSuccess.Should().BeTrue();
        assignment.ServicePeriods.Single(p => p.ServiceId == OriginServiceId)
            .IsInterrupted.Should().BeFalse("nothing to land on → keep the student rotating at the origin");
    }
}
