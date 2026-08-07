using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

// The stage note is the mean of its periods' marks, but the stage is validated ONLY when every
// period is individually validated — one failed period fails the whole stage — and only once every
// period has been evaluated.
public class StageValidationScoringTests
{
    // Drives the real lifecycle: start the assignment, then close every non-interrupted period so the
    // assignment reaches Completed (the state SubmitEvaluation needs to roll up to Evaluated).
    private static InternshipAssignment WithPeriods(int count, out List<ServicePeriod> periods)
    {
        var assignment = new InternshipAssignment { Id = Guid.NewGuid(), CurrentCohortId = 1 };
        periods = [];
        for (int i = 0; i < count; i++)
        {
            var p = new ServicePeriod
            {
                Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id, ServiceId = 10 + i,
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            };
            assignment.ServicePeriods.Add(p);
            periods.Add(p);
        }
        assignment.Start();
        return assignment;
    }

    private static void CloseAllPeriods(InternshipAssignment a)
    {
        foreach (var p in a.ServicePeriods.Where(p => !p.IsInterrupted).ToList())
            a.CompletePeriod(p.Id);
    }

    private static ServiceEvaluation Numeric(decimal mark) =>
        new() { Mode = EvaluationMode.Numeric, TotalScore = mark };

    private static void Evaluate(InternshipAssignment a, ServicePeriod p, ServiceEvaluation e) =>
        a.SubmitEvaluation(p.Id, e).IsSuccess.Should().BeTrue();  // sets p.Evaluation + rolls up the score

    [Fact]
    public void Stage_is_validated_when_all_periods_pass_and_note_is_the_mean()
    {
        var a = WithPeriods(3, out var periods);
        CloseAllPeriods(a);
        // Evaluate each period; the last submit triggers the final roll-up.
        Evaluate(a, periods[0], Numeric(10));
        Evaluate(a, periods[1], Numeric(12));
        Evaluate(a, periods[2], Numeric(14));

        a.FinalScore.Should().Be(12m);
        a.Result.Should().Be(StageAssignmentResult.Validé);
        a.Status.Should().Be(InternshipStatus.Evaluated);
    }

    [Fact]
    public void One_failed_period_fails_the_whole_stage_even_if_the_mean_passes()
    {
        var a = WithPeriods(3, out var periods);
        CloseAllPeriods(a);
        Evaluate(a, periods[0], Numeric(8));   // below 10 → not validated
        Evaluate(a, periods[1], Numeric(12));
        Evaluate(a, periods[2], Numeric(14));

        a.FinalScore.Should().Be(11.33m, "the note is still the mean");
        a.Result.Should().Be(StageAssignmentResult.NonValidé, "one non-valid period sinks the stage");
    }

    [Fact]
    public void Verdict_is_withheld_until_every_period_is_evaluated()
    {
        var a = WithPeriods(3, out var periods);
        CloseAllPeriods(a);
        Evaluate(a, periods[0], Numeric(15));
        Evaluate(a, periods[1], Numeric(16));

        a.Result.Should().Be(StageAssignmentResult.NonÉvalué, "one period is still un-evaluated");
    }

    [Fact]
    public void Validate_only_periods_count_as_ten_and_zero()
    {
        var a = WithPeriods(2, out var periods);
        CloseAllPeriods(a);
        Evaluate(a, periods[0], new ServiceEvaluation
        {
            Mode = EvaluationMode.ValidatePeriod, Outcome = EvaluationOutcome.Validated,
        });
        Evaluate(a, periods[1], new ServiceEvaluation
        {
            Mode = EvaluationMode.ValidatePeriod, Outcome = EvaluationOutcome.NotValidated,
        });

        a.FinalScore.Should().Be(5m, "mean of 10 and 0");
        a.Result.Should().Be(StageAssignmentResult.NonValidé);
    }

    [Fact]
    public void Interrupted_periods_are_excluded_from_the_roll_up()
    {
        var a = WithPeriods(2, out var periods);
        periods[1].IsInterrupted = true;      // cut short by a mid-stage transfer → not graded here
        CloseAllPeriods(a);
        Evaluate(a, periods[0], Numeric(11));

        a.FinalScore.Should().Be(11m);
        a.Result.Should().Be(StageAssignmentResult.Validé, "the only graded period passes");
    }
}
