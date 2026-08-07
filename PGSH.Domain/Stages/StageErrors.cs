using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public static class StageErrors
{
    // === Stage ===
    public static Error NotFound(int stageId) => Error.NotFound(
        "Stages.NotFound",
        $"The stage with Id = '{stageId}' was not found.");

    public static Error DuplicateName(string name) => Error.Conflict(
        "Stages.DuplicateName",
        $"A stage with the name '{name}' already exists.");

    public static readonly Error InvalidDuration = Error.Validation(
        "Stages.InvalidDuration",
        "The stage duration must be greater than zero.");

    public static readonly Error MissingLevel = Error.Validation(
        "Stages.MissingLevel",
        "A level must be assigned to the stage.");

    public static readonly Error InvalidCoefficient = Error.Validation(
        "Stages.InvalidCoefficient",
        "The stage coefficient must be greater than or equal to 1.");

    // === StageGroup ===
    public static Error GroupNotFound(int groupId) => Error.NotFound(
        "StageGroups.NotFound",
        $"The stage group with Id = '{groupId}' was not found.");

    public static Error DuplicateGroupLabel(string label) => Error.Conflict(
        "StageGroups.DuplicateLabel",
        $"A stage group with the label '{label}' already exists.");

    public static readonly Error MissingStageReference = Error.Validation(
        "StageGroups.MissingStageReference",
        "Each stage group must be associated with a valid stage.");

    public static readonly Error EmptyLabel = Error.Validation(
        "StageGroups.EmptyLabel",
        "Stage group label cannot be null or empty.");

    // === InternshipAssignment ===
    public static Error AssignmentNotFound(Guid assignmentId) => Error.NotFound(
        "InternshipAssignments.NotFound",
        $"The internship assignment with Id = '{assignmentId}' was not found.");

    public static readonly Error InvalidDateRange = Error.Validation(
        "InternshipAssignments.InvalidDateRange",
        "The planned end date must be greater than or equal to the start date.");

    public static readonly Error NegativeScore = Error.Validation(
        "InternshipAssignments.NegativeScore",
        "The internship score cannot be negative.");

    public static readonly Error MissingStageGroup = Error.Validation(
        "InternshipAssignments.MissingStageGroup",
        "An internship assignment must be linked to a valid stage group.");

    // === AssignmentPeriod ===
    public static Error PeriodNotFound(Guid periodId) => Error.NotFound(
        "AssignmentPeriods.NotFound",
        $"The assignment period with Id = '{periodId}' was not found.");

    public static readonly Error NotServiceChef = Error.Forbidden(
        "ServicePeriods.NotServiceChef",
        "Vous n'êtes pas chef du service de cette période — vous ne pouvez agir que sur vos propres services.");

    public static readonly Error AdministrativeOnly = Error.Forbidden(
        "ServicePeriods.AdministrativeOnly",
        "Cette ressource est réservée au personnel administratif. Les chefs de service consultent leurs périodes via « Mes services ».");

    public static readonly Error InvalidPeriodRange = Error.Validation(
        "AssignmentPeriods.InvalidPeriodRange",
        "The assignment period end date must be greater than or equal to the start date.");

    public static readonly Error MissingService = Error.Validation(
        "AssignmentPeriods.MissingService",
        "Each assignment period must be associated with a valid hospital service.");

    // === Attendance ===
    public static readonly Error AttendanceDuplicate = Error.Conflict(
        "AttendanceRecords.Duplicate",
        "An attendance record already exists for this date and period.");

    public static readonly Error AttendanceAlreadyGenerated = Error.Conflict(
        "AttendanceRecords.AlreadyGenerated",
        "Attendance records have already been generated for this service period.");

    public static readonly Error AttendanceNotAllowed = Error.Forbidden(
        "AttendanceRecords.NotAllowed",
        "Vous ne pouvez enregistrer ou consulter les présences que pour les périodes de vos propres services.");

    public static readonly Error CannotRerouteAdHocPeriod = Error.Conflict(
        "ServicePeriods.CannotRerouteAdHoc",
        "Cet étudiant a une rotation hors planning (délocalisation ou rattrapage) : elle ne peut pas "
        + "être réaffectée automatiquement au planning du groupe d'accueil.");

    public static readonly Error DelocalizationNotAllowed = Error.Forbidden(
        "Stages.DelocalizationNotAllowed",
        "Seule la scolarité peut enregistrer une délocalisation.");

    public static readonly Error AssignmentReadNotAllowed = Error.Forbidden(
        "InternshipAssignments.ReadNotAllowed",
        "Vous ne pouvez consulter que votre propre dossier de stage, ou celui des étudiants passant "
        + "dans vos services.");

    // === ServiceEvaluation ===
    public static readonly Error EvaluationReadNotAllowed = Error.Forbidden(
        "ServiceEvaluations.ReadNotAllowed",
        "Vous ne pouvez consulter que vos propres notes, ou celles des périodes de vos services.");

    public static Error EvaluationReadOnly(InternshipStatus status) => Error.Conflict(
        "ServiceEvaluations.ReadOnly",
        $"Evaluation cannot be modified because the assignment is already '{status}'.");

    public static Error EvaluationNotFound(Guid periodId) => Error.NotFound(
        "ServiceEvaluations.NotFound",
        $"No evaluation found for service period '{periodId}'.");

    public static Error EvaluationAlreadyExists(Guid periodId) => Error.Conflict(
        "ServiceEvaluations.AlreadyExists",
        $"An evaluation already exists for service period '{periodId}'.");

    public static Error ObjectiveNotInStage(int objectiveId) => Error.Problem(
        "ServiceEvaluations.ObjectiveNotInStage",
        $"L'objectif '{objectiveId}' n'appartient pas au stage de cette période.");

    // === Evaluation import ===
    public static Error ImportPeriodNotInStage(int periodNumber, int stageId) => Error.Problem(
        "ServiceEvaluations.ImportPeriodNotInStage",
        $"Le stage '{stageId}' n'a pas de période P{periodNumber}.");

    public static readonly Error ImportModeNotSupported = Error.Problem(
        "ServiceEvaluations.ImportModeNotSupported",
        "L'import ne gère que la note chiffrée et la validation globale : la validation par objectif "
        + "demande une note par objectif, qui ne tient pas dans une ligne de tableur.");

    public static Error ImportRejected(int errorCount) => Error.Conflict(
        "ServiceEvaluations.ImportRejected",
        $"{errorCount} ligne(s) en erreur — aucune note n'a été enregistrée. "
        + "Un import de notes est appliqué en totalité ou pas du tout.");

    public static readonly Error ImportSheetUnreadable = Error.Problem(
        "ServiceEvaluations.ImportSheetUnreadable",
        "Fichier illisible — attendu un classeur Excel (.xlsx) reprenant les colonnes du modèle.");

    public static readonly Error FicheNotAvailable = Error.Conflict(
        "ServiceEvaluations.FicheNotAvailable",
        "La fiche de validation n'est disponible que lorsque toutes les périodes sont évaluées et le stage validé.");

    // === Cohort ===
    public static Error CohortNotFound(int cohortId) => Error.NotFound(
        "Cohorts.NotFound",
        $"The cohort with Id = '{cohortId}' was not found.");

    // === Schedule / StageSlot ===
    public static Error SlotNotFound(int slotId) => Error.NotFound(
        "Schedule.SlotNotFound",
        $"The stage slot with Id = '{slotId}' was not found.");

    public static Error DuplicatePeriodNumber(int periodNumber) => Error.Conflict(
        "Schedule.DuplicatePeriodNumber",
        $"A slot with period number {periodNumber} already exists for this stage.");

    /// <summary>
    /// Two periods of the same academic level would run at the same time. A level's students follow
    /// every one of its stages, so overlapping windows — whether inside one stage or across two —
    /// would place the same group in two services at once.
    /// </summary>
    public static Error SlotOverlap(
        int periodNumber, DateOnly start, DateOnly end,
        string conflictingStageName, int conflictingPeriodNumber, DateOnly conflictStart, DateOnly conflictEnd,
        bool sameStage) => Error.Conflict(
        "Schedule.SlotOverlap",
        sameStage
            ? $"La période {periodNumber} ({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) chevauche la période "
              + $"{conflictingPeriodNumber} ({conflictStart:dd/MM/yyyy} – {conflictEnd:dd/MM/yyyy}) du même stage. "
              + "Les périodes d'un stage doivent se suivre sans se chevaucher."
            : $"La période {periodNumber} ({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) chevauche la période "
              + $"{conflictingPeriodNumber} du stage « {conflictingStageName} » "
              + $"({conflictStart:dd/MM/yyyy} – {conflictEnd:dd/MM/yyyy}), qui concerne le même niveau. "
              + "Un groupe ne peut pas être affecté à deux stages en même temps.");

    public static Error CapacityExceeded(
        int periodNumber, string serviceName, DateOnly start, DateOnly end, int occupancy, int capacity) => Error.Conflict(
        "Schedule.CapacityExceeded",
        $"Period {periodNumber} cannot be published: service \"{serviceName}\" ({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) already has {occupancy} student(s) and its capacity is {capacity}. Reduce the number of cohorts assigned to this service or choose a service with higher capacity.");

    public static readonly Error ScheduleNotConfigured = Error.Validation(
        "Schedule.NotConfigured",
        "This cohort has no slot assignments configured. Set up the schedule grid before publishing.");

    public static readonly Error ScheduleAlreadyPublished = Error.Conflict(
        "Schedule.AlreadyPublished",
        "This cohort's schedule has already been published. Unpublish it first before making changes.");

    public static readonly Error ScheduleNotPublished = Error.Validation(
        "Schedule.NotPublished",
        "This cohort's schedule has not been published yet. Nothing to unpublish.");

    public static readonly Error SlotPublished = Error.Conflict(
        "Schedule.SlotPublished",
        "This period cannot be deleted because one or more of its cohorts have already been published. Unpublish them first.");

    public static readonly Error NoPlannedAssignments = Error.Validation(
        "Schedule.NoPlannedAssignments",
        "No assignments in 'Planned' status were found for this cohort.");

    public static Error TargetScheduleMissingPeriods(int cohortId, IReadOnlyList<int> periodNumbers) => Error.Conflict(
        "Schedule.TargetScheduleMissingPeriods",
        $"Le groupe cible (cohorte {cohortId}) n'a pas de répartition planifiée pour la/les période(s) {string.Join(", ", periodNumbers)}. Configurez son planning pour ces périodes avant le transfert en cours de stage.");

    // === Allowed services ===
    public static Error ServiceNotAllowed(int serviceId, int stageId) => Error.Conflict(
        "Stages.ServiceNotAllowed",
        $"Service {serviceId} is not in the allowed-services list for stage {stageId}.");

    // === InternshipAssignment lifecycle ===
    public static Error InvalidStatusTransition(string action, InternshipStatus current) => Error.Validation(
        "InternshipAssignments.InvalidStatusTransition",
        $"Cannot '{action}' an assignment in status '{current}'.");

    public static Error PeriodAlreadyComplete(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.AlreadyComplete",
        $"Service period '{periodId}' is already marked as complete.");

    public static Error PeriodAlreadyStarted(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.AlreadyStarted",
        $"Service period '{periodId}' is already started.");

    public static Error PeriodNotComplete(Guid periodId) => Error.Validation(
        "AssignmentPeriods.NotComplete",
        $"Service period '{periodId}' must be completed before submitting an evaluation.");

    public static Error PeriodNotStarted(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.NotStarted",
        $"Service period '{periodId}' must be started before it can be paused.");

    public static Error PeriodAlreadyPaused(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.AlreadyPaused",
        $"Service period '{periodId}' is already paused.");

    public static Error PeriodNotPaused(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.NotPaused",
        $"Service period '{periodId}' is not paused.");

    public static Error PeriodPaused(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.Paused",
        $"Service period '{periodId}' is paused; resume it before closing.");

    public static Error PeriodInterrupted(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.Interrupted",
        $"Service period '{periodId}' was interrupted by a mid-stage transfer; it is terminal history and cannot be started, closed or evaluated.");

    // === Delocalization ===
    public static readonly Error StageAlreadyUnderway = Error.Conflict(
        "Delocalizations.StageAlreadyUnderway",
        "Ce stage est déjà commencé ou clôturé en interne ; la délocalisation concerne un stage effectué entièrement hors faculté.");

    public static readonly Error NoGroupForDelocalization = Error.Conflict(
        "Delocalizations.NoGroup",
        "L'étudiant n'est rattaché à aucun groupe pour cette année ; affectez-le à un groupe avant de délocaliser le stage.");

    public static Error CohortMissingForStage(int stageId) => Error.Conflict(
        "Delocalizations.CohortMissing",
        $"Aucune cohorte n'existe pour le groupe de l'étudiant sur le stage {stageId} ; configurez les cohortes du stage avant de délocaliser.");
}
