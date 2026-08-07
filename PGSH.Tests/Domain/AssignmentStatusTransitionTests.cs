using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

// The assignment status is a state machine: Planned → Ongoing → Completed → Evaluated → Validated
// or Rejected. Every transition guards its precondition rather than silently doing nothing, and the
// two terminal admin verdicts are only reachable once the whole stage is evaluated.
public class AssignmentStatusTransitionTests
{
    private static InternshipAssignment Planned(int periods = 1)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        for (int i = 0; i < periods; i++)
            assignment.ServicePeriods.Add(new ServicePeriod
            {
                Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id, ServiceId = 10 + i,
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            });
        return assignment;
    }

    // Drives the real lifecycle all the way to Evaluated, which is the only state the admin
    // verdicts accept.
    private static InternshipAssignment Evaluated(decimal mark = 14m)
    {
        var a = Planned();
        a.Start().IsSuccess.Should().BeTrue();
        var period = a.ServicePeriods.Single();
        a.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();
        a.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric, TotalScore = mark,
        }).IsSuccess.Should().BeTrue();
        a.Status.Should().Be(InternshipStatus.Evaluated);
        return a;
    }

    [Fact]
    public void A_new_assignment_starts_planned_and_ungraded()
    {
        var a = Planned();

        a.Status.Should().Be(InternshipStatus.Planned);
        a.FinalScore.Should().BeNull();
        a.Result.Should().Be(StageAssignmentResult.NonÉvalué);
    }

    [Fact]
    public void Starting_twice_is_refused()
    {
        var a = Planned();
        a.Start().IsSuccess.Should().BeTrue();

        var result = a.Start();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.InvalidStatusTransition("Start", InternshipStatus.Ongoing));
    }

    [Fact]
    public void Starting_a_single_period_activates_only_that_one()
    {
        var a = Planned(periods: 3);
        var target = a.ServicePeriods.First();

        a.StartPeriod(target.Id).IsSuccess.Should().BeTrue();

        target.IsStarted.Should().BeTrue();
        a.ServicePeriods.Where(p => p.Id != target.Id).Should().OnlyContain(p => !p.IsStarted);
        a.Status.Should().Be(InternshipStatus.Ongoing, "the stage is underway as soon as any period is");
    }

    [Fact]
    public void Starting_the_same_period_twice_is_refused()
    {
        var a = Planned();
        var period = a.ServicePeriods.Single();
        a.StartPeriod(period.Id).IsSuccess.Should().BeTrue();

        var result = a.StartPeriod(period.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodAlreadyStarted(period.Id));
    }

    [Fact]
    public void Starting_an_unknown_period_is_not_found()
    {
        var a = Planned();
        var missing = Guid.NewGuid();

        var result = a.StartPeriod(missing);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotFound(missing));
    }

    [Fact]
    public void Closing_the_same_period_twice_is_refused()
    {
        var a = Planned();
        a.Start().IsSuccess.Should().BeTrue();
        var period = a.ServicePeriods.Single();
        a.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();

        var result = a.CompletePeriod(period.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodAlreadyComplete(period.Id));
    }

    [Fact]
    public void Closing_a_period_raises_its_domain_event()
    {
        var a = Planned();
        a.Start().IsSuccess.Should().BeTrue();
        var period = a.ServicePeriods.Single();

        a.CompletePeriod(period.Id).IsSuccess.Should().BeTrue();

        a.DomainEvents.OfType<ServicePeriodCompletedDomainEvent>()
            .Should().ContainSingle().Which.PeriodId.Should().Be(period.Id);
    }

    [Fact]
    public void An_evaluation_cannot_be_submitted_before_the_period_closes()
    {
        var a = Planned();
        a.Start().IsSuccess.Should().BeTrue();
        var period = a.ServicePeriods.Single();

        var result = a.SubmitEvaluation(period.Id, new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 12m });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.PeriodNotComplete(period.Id));
    }

    [Fact]
    public void A_period_cannot_be_evaluated_twice()
    {
        var a = Evaluated();
        var period = a.ServicePeriods.Single();

        var result = a.SubmitEvaluation(period.Id, new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 18m });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.EvaluationAlreadyExists(period.Id));
    }

    // Ratification is a workflow act, not an academic one: "Valider" officialises the chef's
    // evaluation whatever it says. The academic outcome is owned by the marks alone.

    [Fact]
    public void Ratifying_an_evaluation_makes_it_official_and_raises_the_event()
    {
        var a = Evaluated();

        a.Validate().IsSuccess.Should().BeTrue();

        a.Status.Should().Be(InternshipStatus.Validated);
        a.Result.Should().Be(StageAssignmentResult.Validé, "the chef's 14/20 was already a pass");
        a.DomainEvents.OfType<AssignmentValidatedDomainEvent>()
            .Should().ContainSingle().Which.FinalScore.Should().Be(14m);
    }

    [Fact]
    public void Ratifying_a_failed_stage_records_an_official_failure_not_a_pass()
    {
        var a = Evaluated(mark: 6m);
        a.Result.Should().Be(StageAssignmentResult.NonValidé);

        a.Validate().IsSuccess.Should().BeTrue();

        a.Status.Should().Be(InternshipStatus.Validated, "the evaluation is now official");
        a.Result.Should().Be(StageAssignmentResult.NonValidé,
            "ratifying confirms the chef's verdict — it never converts a failure into a pass");
        a.FinalScore.Should().Be(6m);
    }

    [Fact]
    public void Refusing_to_ratify_moves_the_workflow_without_rewriting_the_marks()
    {
        var a = Evaluated();

        a.Reject().IsSuccess.Should().BeTrue();

        a.Status.Should().Be(InternshipStatus.Rejected);
        a.Result.Should().Be(StageAssignmentResult.Validé,
            "the marks still say 14/20 until they are actually amended");
        a.DomainEvents.OfType<AssignmentRejectedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void A_stage_that_is_not_evaluated_yet_cannot_be_validated()
    {
        var a = Planned();
        a.Start().IsSuccess.Should().BeTrue();

        var result = a.Validate();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.InvalidStatusTransition("Validate", InternshipStatus.Ongoing));
    }

    [Fact]
    public void A_stage_that_is_not_evaluated_yet_cannot_be_rejected()
    {
        var a = Planned();

        var result = a.Reject();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.InvalidStatusTransition("Reject", InternshipStatus.Planned));
    }

    [Fact]
    public void An_already_validated_stage_cannot_be_validated_again()
    {
        var a = Evaluated();
        a.Validate().IsSuccess.Should().BeTrue();

        var result = a.Validate();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StageErrors.InvalidStatusTransition("Validate", InternshipStatus.Validated));
    }

    [Fact]
    public void A_terminal_verdict_is_never_undone_by_a_reschedule()
    {
        var a = Evaluated();
        a.Validate().IsSuccess.Should().BeTrue();

        a.SyncStatusAfterReschedule(new DateOnly(2026, 2, 1));

        a.Status.Should().Be(InternshipStatus.Validated, "an admin decision outranks the group's lifecycle");
    }
}
