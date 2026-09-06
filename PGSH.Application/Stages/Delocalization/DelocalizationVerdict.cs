using PGSH.Application.Stages.Evaluations.Create;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// The verdict an external hospital sent back on paper, in whichever form it sent it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>All three evaluation modes, not just « validé ».</b> A délocalisation used to record a
/// pass/fail and nothing else, so a CHU that returns a note /20 — or a fiche ticked objective by
/// objective — had it flattened to « validé » on the way in, and the number the student actually
/// earned existed nowhere. PGSH does not decide what an external service is able to certify; it
/// records what arrived.</para>
///
/// <para>The shape is <c>CreateServiceEvaluationCommand</c>'s, deliberately: this ends up in the same
/// <see cref="ServiceEvaluation"/>, read by the same <c>StageScoring</c>, shown on the same fiche. A
/// second vocabulary for the same fact is how two screens come to disagree about one mark.</para>
/// </remarks>
public sealed record DelocalizationVerdict(
    EvaluationMode Mode,
    decimal? TotalScore = null,
    EvaluationOutcome? Outcome = null,
    IReadOnlyList<ObjectiveScoreRequest>? ObjectiveScores = null,
    string? SupervisorComment = null,
    string? FicheReference = null);
