using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// A period was graded for the first time. <paramref name="Mark"/> is the period's mark on the 0–20
/// scale as <see cref="StageScoring"/> computes it — NOT the raw <see cref="ServiceEvaluation.TotalScore"/>,
/// which <see cref="ServiceEvaluation.Normalize"/> nulls out for both validate-only modes.
/// </summary>
public sealed record EvaluationSubmittedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    Guid PeriodId,
    decimal Mark) : IDomainEvent;

/// <summary>
/// A mark already on record was corrected — the chef fixes a mistake, or an administrator enters the
/// verdict for a stage the app did not supervise. Carries both marks so the change is auditable.
/// </summary>
public sealed record EvaluationAmendedDomainEvent(
    Guid AssignmentId,
    Guid RegistrationId,
    Guid PeriodId,
    Guid EvaluationId,
    decimal PreviousMark,
    decimal Mark) : IDomainEvent;
