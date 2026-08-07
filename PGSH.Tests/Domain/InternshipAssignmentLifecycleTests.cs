using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

public class InternshipAssignmentLifecycleTests
{
    private static InternshipAssignment WithSinglePlannedPeriod(out ServicePeriod period)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        period = new ServicePeriod
        {
            Id                     = Guid.NewGuid(),
            InternshipAssignmentId = assignment.Id,
            ServiceId              = 10,
            StartDate              = new DateOnly(2026, 1, 1),
            EndDate                = new DateOnly(2026, 1, 31),
        };
        assignment.ServicePeriods.Add(period);
        return assignment;
    }

    [Fact]
    public void Start_activates_every_period_and_moves_to_ongoing()
    {
        var assignment = WithSinglePlannedPeriod(out var period);

        var result = assignment.Start();

        result.IsSuccess.Should().BeTrue();
        assignment.Status.Should().Be(InternshipStatus.Ongoing);
        period.IsStarted.Should().BeTrue();
    }

    private static readonly DateOnly Today = new(2026, 3, 1);

    [Fact]
    public void SyncStatusAfterReschedule_promotes_a_planned_assignment_with_a_running_period()
    {
        // Simulates a transfer that materialised a period already running in the target group: the
        // rescheduler set IsStarted directly, so the assignment must be pulled out of "Planned".
        var assignment = WithSinglePlannedPeriod(out var period);
        period.IsStarted = true;

        assignment.SyncStatusAfterReschedule(Today);

        assignment.Status.Should().Be(InternshipStatus.Ongoing);
    }

    [Fact]
    public void SyncStatusAfterReschedule_marks_completed_when_joining_an_already_closed_group()
    {
        // The student lands in a group whose current period is already clôturé: his materialised period
        // is closed too, so the assignment must reach Completed (so its evaluations can roll up).
        var assignment = WithSinglePlannedPeriod(out var period);
        period.IsStarted = true;
        period.IsComplete = true;

        assignment.SyncStatusAfterReschedule(Today);

        assignment.Status.Should().Be(InternshipStatus.Completed);
    }

    [Fact]
    public void SyncStatusAfterReschedule_ignores_an_interrupted_started_period()
    {
        var assignment = WithSinglePlannedPeriod(out var period);
        period.IsStarted = true;
        period.IsInterrupted = true;   // terminal history, not an active rotation

        assignment.SyncStatusAfterReschedule(Today);

        assignment.Status.Should().Be(InternshipStatus.Planned);
    }

    [Fact]
    public void CompletePeriod_marks_assignment_completed_when_all_periods_done()
    {
        var assignment = WithSinglePlannedPeriod(out var period);
        assignment.Start();

        var result = assignment.CompletePeriod(period.Id);

        result.IsSuccess.Should().BeTrue();
        period.IsComplete.Should().BeTrue();
        assignment.Status.Should().Be(InternshipStatus.Completed);
    }
}
