namespace PGSH.Domain.Stages;

/// <summary>
/// The amendable content of a <see cref="ServiceEvaluation"/>, handed to
/// <see cref="InternshipAssignment.AmendEvaluation"/>. Keeping it separate from the entity makes it
/// explicit that a correction replaces the marks wholesale — including the per-objective ones — and
/// keeps the identity, the period link and the creation audit out of the caller's reach.
/// </summary>
/// <param name="ObjectiveScores">
/// The replacement per-objective marks. Their <c>Id</c> must be left unset: the aggregate adds them to
/// a tracked evaluation, where a pre-set store-generated key is classified as an update, not an insert.
/// </param>
public sealed record EvaluationAmendment(
    EvaluationMode Mode,
    decimal? TotalScore,
    EvaluationOutcome? Outcome,
    string? SupervisorComment,
    string? FicheReference,
    Guid? EvaluatedByUserId,
    DateTime EvaluatedAt,
    IReadOnlyList<ObjectiveScore> ObjectiveScores);
