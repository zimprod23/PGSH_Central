using FluentAssertions;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Domain;

// The per-period mark/verdict rules shared by the domain roll-up and the read handlers.
public class StageScoringTests
{
    [Fact]
    public void Numeric_without_objectives_uses_the_total_score()
    {
        var e = new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 13.5m };

        StageScoring.PeriodMark(e).Should().Be(13.5m);
        StageScoring.IsPeriodValidated(e).Should().BeTrue();
    }

    [Fact]
    public void Numeric_with_objectives_is_the_weighted_average_of_scores()
    {
        var e = new ServiceEvaluation
        {
            Mode = EvaluationMode.Numeric,
            ObjectiveScores =
            [
                new ObjectiveScore { Score = 8,  StageObjective = new StageObjective { Weight = 1 } },
                new ObjectiveScore { Score = 16, StageObjective = new StageObjective { Weight = 3 } },
            ],
        };

        // (8*1 + 16*3) / (1+3) = 56/4 = 14
        StageScoring.PeriodMark(e).Should().Be(14m);
        StageScoring.IsPeriodValidated(e).Should().BeTrue();
    }

    [Fact]
    public void Numeric_below_ten_is_not_validated()
    {
        var e = new ServiceEvaluation { Mode = EvaluationMode.Numeric, TotalScore = 9.99m };

        StageScoring.IsPeriodValidated(e).Should().BeFalse();
    }

    [Theory]
    [InlineData(EvaluationOutcome.Validated, 10, true)]
    [InlineData(EvaluationOutcome.NotValidated, 0, false)]
    public void Validate_only_verdict_maps_to_ten_or_zero(EvaluationOutcome outcome, int mark, bool validated)
    {
        var period    = new ServiceEvaluation { Mode = EvaluationMode.ValidatePeriod,     Outcome = outcome };
        var objectives = new ServiceEvaluation { Mode = EvaluationMode.ValidateObjectives, Outcome = outcome };

        StageScoring.PeriodMark(period).Should().Be(mark);
        StageScoring.IsPeriodValidated(period).Should().Be(validated);
        StageScoring.PeriodMark(objectives).Should().Be(mark);
        StageScoring.IsPeriodValidated(objectives).Should().Be(validated);
    }
}
