using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

// A period cut short by a mid-stage transfer (IsInterrupted) is terminal history: the origin chef
// may see it but can never re-open, close or evaluate it. These guards keep it out of the lifecycle
// so a bulk stage close (which selects every !IsComplete period) can't silently revive it.
public class InterruptedPeriodTests
{
    private static (InternshipAssignment, ServicePeriod) InterruptedInFlight()
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 2 };
        var period = new ServicePeriod
        {
            Id                     = Guid.NewGuid(),
            InternshipAssignmentId = assignment.Id,
            ServiceId              = 10,
            StartDate              = new DateOnly(2026, 1, 1),
            EndDate                = new DateOnly(2026, 1, 15),
            IsStarted              = true,
            IsInterrupted          = true,
        };
        assignment.ServicePeriods.Add(period);
        return (assignment, period);
    }

    [Fact]
    public void CompletePeriod_refuses_an_interrupted_period()
    {
        var (assignment, period) = InterruptedInFlight();

        var result = assignment.CompletePeriod(period.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AssignmentPeriods.Interrupted");
        period.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void PausePeriod_refuses_an_interrupted_period()
    {
        var (assignment, period) = InterruptedInFlight();

        var result = assignment.PausePeriod(period.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AssignmentPeriods.Interrupted");
        period.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void SubmitEvaluation_cannot_target_an_interrupted_period()
    {
        // Interrupted periods never reach IsComplete, and evaluation requires a completed period,
        // so an interrupted rotation can never be evaluated by the old chef.
        var (assignment, period) = InterruptedInFlight();

        var result = assignment.SubmitEvaluation(period.Id, new ServiceEvaluation());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AssignmentPeriods.NotComplete");
    }
}
