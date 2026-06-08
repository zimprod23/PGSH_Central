namespace PGSH.Domain.Stages;

/// <summary>
/// How a chef records a <see cref="ServiceEvaluation"/>. Not every chef scores numerically:
/// some only certify that the period (or each objective) was validated.
/// </summary>
public enum EvaluationMode
{
    /// <summary>0–20 weighted grade per objective (or a single final note). Produces a <c>FinalScore</c>.</summary>
    Numeric,

    /// <summary>Pass/fail verdict on the whole period, no numeric score.</summary>
    ValidatePeriod,

    /// <summary>Pass/fail verdict on each objective; the period outcome is derived from them.</summary>
    ValidateObjectives,
}

/// <summary>Pass/fail verdict carried by validate-mode evaluations and per-objective in <see cref="EvaluationMode.ValidateObjectives"/>.</summary>
public enum EvaluationOutcome
{
    Validated,
    NotValidated,
}
