namespace PGSH.Domain.Stages;

/// <summary>
/// Single source of truth for turning one period's <see cref="ServiceEvaluation"/> into a numeric
/// mark and a pass/fail verdict, and for rolling the periods up into the stage result. Shared by the
/// domain (<see cref="InternshipAssignment.RecalculateFinalScore"/>) and the read handlers (student
/// stage record + fiche de validation) so they never drift.
///
/// Rules (locked with user):
///   • a period's mark: numeric = objective-weighted average of its scores (or the bare
///     <see cref="ServiceEvaluation.TotalScore"/> when it has no objectives); a validate-only verdict
///     maps onto the 0–20 scale as validated = 10, not validated = 0;
///   • a period is validated: numeric ⇔ mark ≥ 10; validate-only ⇔ Outcome = Validated;
///   • the stage's final note is the plain mean of its periods' marks;
///   • the stage is validated ONLY when every period is individually validated — one failed period
///     fails the whole stage — and only once every (non-interrupted) period has been evaluated.
/// </summary>
public static class StageScoring
{
    public const decimal ValidationThreshold = 10m;

    public static decimal PeriodMark(ServiceEvaluation evaluation) => evaluation.Mode switch
    {
        EvaluationMode.ValidatePeriod     => OutcomeMark(evaluation.Outcome),
        EvaluationMode.ValidateObjectives => OutcomeMark(evaluation.Outcome),
        _                                 => NumericMark(evaluation),
    };

    public static bool IsPeriodValidated(ServiceEvaluation evaluation) => evaluation.Mode switch
    {
        EvaluationMode.Numeric => PeriodMark(evaluation) >= ValidationThreshold,
        _                      => evaluation.Outcome == EvaluationOutcome.Validated,
    };

    private static decimal OutcomeMark(EvaluationOutcome? outcome) =>
        outcome == EvaluationOutcome.Validated ? ValidationThreshold : 0m;

    private static decimal NumericMark(ServiceEvaluation evaluation)
    {
        if (evaluation.ObjectiveScores.Count == 0)
            return evaluation.TotalScore ?? 0m;

        decimal weightedSum = 0, totalWeight = 0;
        foreach (var o in evaluation.ObjectiveScores)
        {
            decimal weight = o.StageObjective?.Weight ?? 1;
            weightedSum += (o.Score ?? 0) * weight;
            totalWeight += weight;
        }

        return totalWeight > 0
            ? Math.Round(weightedSum / totalWeight, 2)
            : evaluation.TotalScore ?? 0m;
    }
}
