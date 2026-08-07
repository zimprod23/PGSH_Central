using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

// A rotation suspended mid-flight (an exam week) is frozen, not lost: the chef sees it as paused and
// cannot act on it, and on resume the days lost extend the period so the student still serves the
// stage in full — every later period of the same assignment shifts by the same amount.
public class PeriodPauseResumeTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End   = new(2026, 1, 31);

    private static InternshipAssignment WithStartedPeriod(out ServicePeriod period)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        period = NewPeriod(assignment.Id, Start, End);
        assignment.ServicePeriods.Add(period);
        assignment.Start().IsSuccess.Should().BeTrue();
        return assignment;
    }

    private static ServicePeriod NewPeriod(Guid assignmentId, DateOnly start, DateOnly end) => new()
    {
        Id = Guid.NewGuid(), InternshipAssignmentId = assignmentId, ServiceId = 10,
        StartDate = start, EndDate = end,
    };

    [Fact]
    public void Pausing_a_running_period_freezes_it_and_opens_a_pause_record()
    {
        var a = WithStartedPeriod(out var p);

        var result = a.PausePeriod(p.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, "Semaine d'examens");

        result.IsSuccess.Should().BeTrue();
        p.IsPaused.Should().BeTrue();
        p.Pauses.Should().ContainSingle().Which.ResumeDate.Should().BeNull();
        p.Pauses.Single().Reason.Should().Be("Semaine d'examens");
        p.Pauses.Single().Kind.Should().Be(PauseKind.Exam);
    }

    [Fact]
    public void Pausing_refuses_a_period_that_has_not_started()
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        var p = NewPeriod(assignment.Id, Start, End);
        assignment.ServicePeriods.Add(p);

        var result = assignment.PausePeriod(p.Id, Start, PauseKind.Exam, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotStarted(p.Id));
    }

    [Fact]
    public void Pausing_refuses_an_already_closed_period()
    {
        var a = WithStartedPeriod(out var p);
        a.CompletePeriod(p.Id).IsSuccess.Should().BeTrue();

        var result = a.PausePeriod(p.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodAlreadyComplete(p.Id));
    }

    [Fact]
    public void Pausing_twice_is_refused()
    {
        var a = WithStartedPeriod(out var p);
        a.PausePeriod(p.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, null).IsSuccess.Should().BeTrue();

        var result = a.PausePeriod(p.Id, new DateOnly(2026, 1, 12), PauseKind.Exam, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodAlreadyPaused(p.Id));
    }

    [Fact]
    public void Pausing_an_unknown_period_is_not_found()
    {
        var a = WithStartedPeriod(out _);
        var missing = Guid.NewGuid();

        var result = a.PausePeriod(missing, Start, PauseKind.Exam, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotFound(missing));
    }

    [Fact]
    public void A_paused_period_cannot_be_closed_until_it_resumes()
    {
        var a = WithStartedPeriod(out var p);
        a.PausePeriod(p.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, null).IsSuccess.Should().BeTrue();

        var result = a.CompletePeriod(p.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodPaused(p.Id));
    }

    [Fact]
    public void Resuming_closes_the_pause_and_extends_the_period_by_the_days_lost()
    {
        var a = WithStartedPeriod(out var p);
        a.PausePeriod(p.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, null).IsSuccess.Should().BeTrue();

        var result = a.ResumePeriod(p.Id, new DateOnly(2026, 1, 17));   // 7 days lost

        result.IsSuccess.Should().BeTrue();
        p.IsPaused.Should().BeFalse();
        p.Pauses.Single().ResumeDate.Should().Be(new DateOnly(2026, 1, 17));
        p.EndDate.Should().Be(End.AddDays(7), "the student must still serve the full rotation");
    }

    [Fact]
    public void Resuming_pushes_every_later_period_forward_by_the_same_amount()
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        var first  = NewPeriod(assignment.Id, Start, End);
        var second = NewPeriod(assignment.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));
        assignment.ServicePeriods.Add(first);
        assignment.ServicePeriods.Add(second);
        assignment.Start().IsSuccess.Should().BeTrue();

        assignment.PausePeriod(first.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, null).IsSuccess.Should().BeTrue();
        assignment.ResumePeriod(first.Id, new DateOnly(2026, 1, 17)).IsSuccess.Should().BeTrue();

        second.StartDate.Should().Be(new DateOnly(2026, 2, 8), "the rotation stays contiguous");
        second.EndDate.Should().Be(new DateOnly(2026, 3, 7));
    }

    [Fact]
    public void Resuming_on_the_same_day_shifts_nothing()
    {
        var a = WithStartedPeriod(out var p);
        var pauseDate = new DateOnly(2026, 1, 10);
        a.PausePeriod(p.Id, pauseDate, PauseKind.Exam, null).IsSuccess.Should().BeTrue();

        a.ResumePeriod(p.Id, pauseDate).IsSuccess.Should().BeTrue();

        p.IsPaused.Should().BeFalse();
        p.EndDate.Should().Be(End, "no day was actually lost");
    }

    [Fact]
    public void Resuming_a_period_that_is_not_paused_is_refused()
    {
        var a = WithStartedPeriod(out var p);

        var result = a.ResumePeriod(p.Id, new DateOnly(2026, 1, 17));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotPaused(p.Id));
    }

    // A resume used to push EVERY later period forward, closed and interrupted ones included, so
    // resuming back-dated the rotations the student had already finished.
    [Fact]
    public void Resuming_never_moves_a_rotation_that_is_already_over()
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        var running = NewPeriod(assignment.Id, Start, End);
        var closed  = NewPeriod(assignment.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));
        var cutShort = NewPeriod(assignment.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        var upcoming = NewPeriod(assignment.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        assignment.ServicePeriods.Add(running);
        assignment.ServicePeriods.Add(closed);
        assignment.ServicePeriods.Add(cutShort);
        assignment.ServicePeriods.Add(upcoming);
        assignment.Start().IsSuccess.Should().BeTrue();

        closed.IsComplete = true;          // history: these dates are what actually happened
        cutShort.IsInterrupted = true;     // terminal: cut by a mid-stage transfer

        assignment.PausePeriod(running.Id, new DateOnly(2026, 1, 10), PauseKind.Exam, null)
            .IsSuccess.Should().BeTrue();
        assignment.ResumePeriod(running.Id, new DateOnly(2026, 1, 15)).IsSuccess.Should().BeTrue();

        closed.StartDate.Should().Be(new DateOnly(2026, 2, 1), "a closed rotation is history");
        closed.EndDate.Should().Be(new DateOnly(2026, 2, 28));
        cutShort.StartDate.Should().Be(new DateOnly(2026, 3, 1), "an interrupted rotation is terminal");
        upcoming.StartDate.Should().Be(new DateOnly(2026, 4, 6), "only what is still ahead moves");
        upcoming.EndDate.Should().Be(new DateOnly(2026, 5, 5));
    }

    // PausePeriod refused a rotation that never began; CompletePeriod did not, so a stage nobody ran
    // could be closed and then graded.
    [Fact]
    public void A_rotation_that_never_started_cannot_be_closed()
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        var running = NewPeriod(assignment.Id, Start, End);
        var future  = NewPeriod(assignment.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));
        assignment.ServicePeriods.Add(running);
        assignment.ServicePeriods.Add(future);
        assignment.StartPeriod(running.Id).IsSuccess.Should().BeTrue();

        var result = assignment.CompletePeriod(future.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotStarted(future.Id));
        future.IsComplete.Should().BeFalse();
    }
}
