using PGSH.Application.Calendar.Pauses;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.InternshipAssignments;

public sealed record InternshipAssignmentSummaryResponse(
    Guid Id,
    Guid RegistrationId,
    string StudentFullName,
    int CohortId,
    string CohortLabel,
    int StageId,
    string StageName,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result,
    // True while any of the assignment's active periods is suspended (e.g. an exam week). The
    // assignment Status itself stays Ongoing — this surfaces the per-period pause on the row.
    bool IsPaused = false,
    // True once every (non-interrupted) period has an evaluation — only then is FinalScore/Result a
    // final stage verdict worth showing in the notes list.
    bool AllPeriodsEvaluated = false,
    // True when the stage is served outside the faculty. Carried on the row because the two acts a
    // screen can offer are opposites — délocaliser, or annuler la délocalisation — and the status
    // alone cannot tell them apart: a délocalisation is Completed, exactly like a stage served here.
    bool IsDelocalized = false,
    // La fenêtre que la promotion a déclarée et qui couvre *aujourd'hui*, quand la rotation est en
    // cours — sinon null.
    //
    // ⚠ Ce n'est pas IsPaused sous un autre nom, et les deux coexistent exprès. IsPaused est un
    // drapeau *stocké* que plus aucun acte ne pose depuis le 18/09/2026 et qu'une annulation de
    // téléversement peut encore remettre ; celui-ci est **dérivé** du calendrier de la promotion à
    // chaque lecture. Révoquer la fenêtre l'éteint pour toute la promotion d'un coup, sans un écrit —
    // ce qui est exactement ce que l'acte retiré la veille ne savait pas faire.
    //
    // ⚠ Et il remplace le statut à l'écran plutôt que de s'y ajouter : « En cours » est vrai du cycle
    // de vie et faux de l'endroit où l'étudiant est ce matin. Mesuré le 18/09/2026 : 472 rotations se
    // lisaient « en service » pendant une fenêtre d'examens déclarée.
    PromotionSuspension? SuspendedBy = null);

public sealed record InternshipAssignmentResponse(
    Guid Id,
    Guid RegistrationId,
    string StudentFullName,
    int CohortId,
    string CohortLabel,
    InternshipStatus Status,
    decimal? FinalScore,
    StageAssignmentResult? Result,
    IReadOnlyList<ServicePeriodSummary> ServicePeriods,
    // La fenêtre déclarée par la promotion de cet étudiant qui couvre *aujourd'hui* — sinon null.
    //
    // ⚠ <b>Au niveau de l'affectation, et pas seulement de ses périodes.</b> Signalé depuis l'écran le
    // 23/09/2026 : « sur la rotation on voit En examens, sur le badge du stage on ne voit qu'En
    // cours ». Les périodes la portaient, l'affectation non — donc le même dossier donnait deux
    // réponses à une ligne d'intervalle, et celle du haut était la fausse.
    //
    // ⚠ Le critère est celui de la *ligne d'affectation*, pas celui d'une période : l'étudiant compose
    // quelles que soient les dates de tel ou tel séjour. Même règle que
    // <c>InternshipAssignmentSummaryResponse</c>, et c'est voulu — une seule question, une seule
    // réponse, quel que soit l'écran qui la pose.
    PromotionSuspension? SuspendedBy = null);

public sealed record ServicePeriodSummary(
    Guid Id,
    int ServiceId,
    string ServiceName,
    string HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsComplete,
    bool HasEvaluation,
    bool IsStarted = false,
    bool IsPaused = false,
    string? PauseReason = null,
    // The whole stage was served outside the faculty: this is an ad-hoc, pre-completed period
    // evaluated from the paper fiche de validation. No in-app chef supervises it.
    bool IsDelocalized = false,
    string? DelocalizationReason = null,
    // La fenêtre déclarée par la promotion de cet étudiant qui couvre *aujourd'hui* — sinon null.
    //
    // ⚠ C'est la même donnée que sur la ligne d'affectation, portée jusqu'ici parce que le portail
    // étudiant lit ce chemin et aucun autre : une suspension visible côté administration et muette sur
    // le dossier de l'étudiant serait la règle avec deux réponses selon qui regarde.
    PromotionSuspension? SuspendedBy = null);
