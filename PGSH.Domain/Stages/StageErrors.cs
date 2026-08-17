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
        $"Le stage '{stageId}' n'a pas de période P{periodNumber} pour l'année sélectionnée.");

    /// <summary>
    /// The stage ran in other years but not the one being imported. Distinguished from an empty stage
    /// because the fix differs: switch the year in the navbar, rather than plan the stage.
    /// </summary>
    public static Error ImportYearHasNoStudents(int stageId, string yearLabel) => Error.Problem(
        "ServiceEvaluations.ImportYearHasNoStudents",
        $"Aucun étudiant n'est affecté au stage '{stageId}' pour l'année {yearLabel}.");

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

    /// <summary>
    /// A cohorte is a roster <i>doing one stage</i>, so both ends have to belong to the same
    /// promotion. Building one across promotions plans a roster into a stage set that is not its own,
    /// and — because a cell's level is read off <c>Cohort.Stage.LevelId</c> — books it against the
    /// wrong promotion's service quota.
    /// </summary>
    /// <remarks>
    /// <c>CohortProvisioner</c> has always checked this; the hand-built path had no equivalent, which
    /// left a cohorte across promotions reachable by a single POST.
    /// </remarks>
    public static Error CohortPromotionMismatch(
        string groupLabel, string groupLevel, string stageName, string stageLevel) => Error.Validation(
        "Cohorts.PromotionMismatch",
        $"« {groupLabel} » est un groupe de {groupLevel}, mais « {stageName} » est un stage de "
        + $"{stageLevel}. Une cohorte ne peut pas relier deux promotions.");

    /// <summary>
    /// « Non réparti » holds every promotion's unassigned registrations, so a cohorte on it would put
    /// all of them through one stage in one service — see <c>AcademicGroupErrors</c>.
    /// </summary>
    public static Error CohortOnUnassignedRoster(string groupLabel) => Error.Validation(
        "Cohorts.OnUnassignedRoster",
        $"« {groupLabel} » n'appartient à aucune promotion : il rassemble les inscriptions non "
        + "réparties de toutes les promotions de l'année. Répartissez ces étudiants dans des groupes "
        + "avant de créer une cohorte.");

    // === Schedule / StageSlot ===
    public static Error SlotNotFound(int slotId) => Error.NotFound(
        "Schedule.SlotNotFound",
        $"The stage slot with Id = '{slotId}' was not found.");

    public static Error DuplicatePeriodNumber(int periodNumber) => Error.Conflict(
        "Schedule.DuplicatePeriodNumber",
        $"A slot with period number {periodNumber} already exists for this stage in this academic year.");

    public static Error AcademicYearNotFound(int academicYearId) => Error.NotFound(
        "AcademicYears.NotFound",
        $"The academic year with Id = '{academicYearId}' was not found.");

    /// <summary>
    /// No year could be resolved for an operation that is year-scoped. Reached only when the caller
    /// passed none and no year is flagged current — never silently widened to "all years", which is
    /// what made the import canvas list every promotion the stage ever had.
    /// </summary>
    public static readonly Error NoCurrentAcademicYear = Error.Problem(
        "AcademicYears.NoCurrent",
        "Aucune année universitaire courante n'est définie — sélectionnez une année.");

    /// <summary>
    /// Two periods <b>of one stage</b> would run at the same time. Its cohorts rotate through those
    /// periods in sequence, so overlapping windows are always a mistake.
    ///
    /// Two <i>different</i> stages of the same level may share a window — that is how a promotion
    /// split into partitions is planned, and it is guarded per group by
    /// <c>GroupScheduleConflictGuard</c> instead.
    /// </summary>
    public static Error SlotOverlap(
        int periodNumber, DateOnly start, DateOnly end,
        int conflictingPeriodNumber, DateOnly conflictStart, DateOnly conflictEnd) => Error.Conflict(
        "Schedule.SlotOverlap",
        $"La période {periodNumber} ({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) chevauche la période "
        + $"{conflictingPeriodNumber} ({conflictStart:dd/MM/yyyy} – {conflictEnd:dd/MM/yyyy}) du même stage. "
        + "Les périodes d'un stage doivent se suivre sans se chevaucher.");

    /// <summary>
    /// A partition was targeted on a promotion that has never been divided into any. Distinct from
    /// "that partition holds no cohort here", which is legitimate and stays silent — this one means
    /// the caller is naming a division that does not exist, and no arrangement can follow from it.
    /// </summary>
    public static Error PromotionNotPartitioned(string stageName, string levelLabel) => Error.Validation(
        "Schedule.PromotionNotPartitioned",
        $"Aucun groupe de « {levelLabel} » ne porte de partition, donc « {stageName} » ne peut pas être "
        + "réparti par partition. Découpez d'abord la promotion (Groupes → Planification Macro), "
        + "ou lancez la répartition sans cibler de partition.");

    /// <summary>
    /// A group would sit in two services at once: it is already placed in a period whose dates
    /// overlap the one being assigned. This is the real double-booking rule — it names the group,
    /// so it can tell the legitimate case (partition A in Médecine P1, partition B in Chirurgie P1,
    /// same dates) from the mistake (group 1 in both).
    /// </summary>
    /// <remarks>
    /// ⚠ It names the <b>promotion</b> too, because the collision is not always within the one being
    /// planned. Legacy groups are numbered per year rather than per (year, level), so one row can
    /// carry the 3rd year's group 1 and the 5th year's — and then planning the 5th year is refused by
    /// a placement made for the 3rd. "Déjà affecté au stage « Chirurgie »" sends the admin hunting
    /// through a promotion that has no Chirurgie; the level label is what makes it legible.
    /// </remarks>
    public static Error GroupAlreadyPlaced(
        int groupNumber, string stageName, string levelLabel,
        int periodNumber, DateOnly start, DateOnly end) => Error.Conflict(
        "Schedule.GroupAlreadyPlaced",
        $"Le groupe {groupNumber} est déjà affecté au stage « {stageName} » ({levelLabel}) "
        + $"période {periodNumber} ({start:dd/MM/yyyy} – {end:dd/MM/yyyy}), qui chevauche cette période. "
        + "Un groupe ne peut pas être dans deux services en même temps — ciblez une autre partition.");

    public static Error CapacityExceeded(
        int periodNumber, string serviceName, DateOnly start, DateOnly end, int occupancy, int capacity) => Error.Conflict(
        "Schedule.CapacityExceeded",
        $"Period {periodNumber} cannot be published: service \"{serviceName}\" ({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) already has {occupancy} student(s) and its capacity is {capacity}. Reduce the number of cohorts assigned to this service or choose a service with higher capacity.");

    /// <summary>
    /// The service has room overall but not for <i>this</i> promotion. Named separately from
    /// <see cref="CapacityExceeded"/> because the remedy is different: the total is relieved by
    /// moving anyone out, the quota only by moving out students of that level — or by raising
    /// the quota, which is a decision the service makes, not the planner.
    /// </summary>
    public static Error LevelCapacityExceeded(
        int periodNumber, string serviceName, string levelLabel,
        DateOnly start, DateOnly end, int occupancy, int capacity) => Error.Conflict(
        "Schedule.LevelCapacityExceeded",
        $"La période {periodNumber} ne peut pas être publiée : le service « {serviceName} » "
        + $"({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) accueillerait {occupancy} étudiant(s) de {levelLabel} "
        + $"alors que son quota pour cette promotion est de {capacity}. Réduisez le nombre de groupes de "
        + "cette promotion affectés à ce service, ou augmentez son quota depuis la fiche du service.");

    /// <summary>
    /// The service does not take this promotion at all — it carries intake rules and none of them
    /// name this level. Distinct from a quota of zero only in wording; both refuse, but this one
    /// tells the planner the service was never a candidate rather than that it is full.
    /// </summary>
    public static Error LevelNotAdmitted(
        int periodNumber, string serviceName, string levelLabel,
        DateOnly start, DateOnly end) => Error.Conflict(
        "Schedule.LevelNotAdmitted",
        $"La période {periodNumber} ne peut pas être publiée : le service « {serviceName} » "
        + $"({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) n'accueille pas les étudiants de {levelLabel}. "
        + "Choisissez un autre service, ou ajoutez un quota pour cette promotion depuis la fiche du service.");

    /// <summary>
    /// Every service the stage allows refuses its level. Raised by auto-arrange rather than
    /// silently producing an empty grid, because "no services" and "no services <i>for you</i>"
    /// send the user to two different screens.
    /// </summary>
    public static Error NoServicesAdmitLevel(string stageName, string levelLabel) => Error.Validation(
        "Schedule.NoServicesAdmitLevel",
        $"Aucun des services autorisés pour le stage « {stageName} » n'accueille les étudiants de {levelLabel}. "
        + "Ajoutez un quota pour cette promotion sur au moins un de ces services, ou élargissez la liste "
        + "des services autorisés du stage.");

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

    /// <summary>
    /// Unpublishing deletes <see cref="ServicePeriod"/>s, and evaluations, attendance, pauses and
    /// délocalisations all cascade from them. Once a rotation has actually begun that is no longer
    /// the inverse of publishing — it is the destruction of what happened — so the caller has to say
    /// so explicitly, and the refusal names what would be lost rather than a bare "cannot".
    /// </summary>
    public static Error ScheduleUnderway(
        int periods, int started, int evaluated, int attendanceDays) => Error.Conflict(
        "Schedule.Underway",
        $"Cette répartition est déjà engagée : sur {periods} période(s) publiée(s), {started} ont démarré, "
        + $"{evaluated} portent une évaluation et {attendanceDays} journée(s) de présence sont enregistrées. "
        + "Dépublier les supprimerait définitivement. Confirmez explicitement pour continuer.");

    public static Error RotationModeLockedByPublication(string stageName) => Error.Conflict(
        "Stages.RotationModeLockedByPublication",
        $"Le mode de rotation du stage « {stageName} » ne peut plus être modifié : sa répartition est "
        + "publiée, et les périodes déjà créées suivent le mode actuel. Dépubliez la répartition de ce "
        + "stage avant de changer le mode.");

    /// <summary>
    /// A single-service stage is arranged run by run, so the caller must say which périodes make up
    /// the run. Left unscoped, "arrange the whole stage" would put a cohort in one service for every
    /// column the stage owns — a year in one service, silently.
    /// </summary>
    public static Error SingleServiceRunNotScoped(string stageName, int slotCount) => Error.Validation(
        "Schedule.SingleServiceRunNotScoped",
        $"Le stage « {stageName} » se déroule dans un seul service par passage, et compte {slotCount} périodes "
        + "sur l'axe. Précisez les périodes du passage à répartir (par exemple P1 à P3) : sans cela, un groupe "
        + "serait affecté à un seul service pour la totalité des périodes du stage.");

    /// <summary>
    /// The run handed to the arranger is not a contiguous block of the axis. A single stay cannot
    /// have a hole in it, and the dates of the resulting period would silently span the gap.
    /// </summary>
    public static Error SingleServiceRunNotContiguous(
        string stageName, IReadOnlyList<int> periodNumbers) => Error.Validation(
        "Schedule.SingleServiceRunNotContiguous",
        $"Les périodes {string.Join(", ", periodNumbers)} du stage « {stageName} » ne se suivent pas. "
        + "Un passage en un seul service doit couvrir des périodes consécutives.");

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

    /// <summary>
    /// The service carries intake quotas and none of them names this stage's promotion, so it could
    /// never host the stage. Refused at the moment the list is authored rather than left for
    /// auto-arrange to skip and publish to reject — those happen weeks later, to someone else.
    /// </summary>
    public static Error ServiceDoesNotAdmitStageLevel(
        string serviceName, string levelLabel, IReadOnlyList<string> admittedLevels) => Error.Conflict(
        "Stages.ServiceDoesNotAdmitStageLevel",
        $"Le service « {serviceName} » n'accueille pas les étudiants de {levelLabel}, et ne peut donc pas "
        + $"être autorisé pour ce stage. Il est réservé à : {string.Join(", ", admittedLevels)}. "
        + "Ajoutez un quota pour cette promotion depuis la fiche du service, ou choisissez un autre service.");

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

    // === Dossier de niveau ===
    public static readonly Error DossierReadNotAllowed = Error.Forbidden(
        "StudentDossier.ReadNotAllowed",
        "Le dossier de niveau retrace toutes les inscriptions d'un étudiant : il est réservé à la "
        + "scolarité et à l'étudiant lui-même.");

    // === Revalidation ===
    public static readonly Error RevalidationNotAllowed = Error.Forbidden(
        "Revalidations.NotAllowed",
        "Seule la scolarité peut ouvrir un stage en revalidation.");

    public static readonly Error NoGroupForRevalidation = Error.Conflict(
        "Revalidations.NoGroup",
        "L'étudiant n'est rattaché à aucun groupe pour cette inscription ; affectez-le à un groupe "
        + "avant d'ouvrir une revalidation.");

    public static Error CohortNotForStage(int cohortId, int stageId) => Error.Validation(
        "Revalidations.CohortNotForStage",
        $"La cohorte {cohortId} ne concerne pas le stage {stageId}.");

    /// <summary>
    /// No cohort exists for the student's own group on this stage. Routine when the stage belongs to an
    /// earlier level — a 6th-year student redoing a 1st-year stage has no 1st-year group — so the
    /// message points at the way out: name the cohort to join explicitly.
    /// </summary>
    public static Error NoCohortForRevalidation(int stageId) => Error.Conflict(
        "Revalidations.NoCohortForGroup",
        $"Aucune cohorte n'existe pour le groupe de l'étudiant sur le stage {stageId}. C'est le cas "
        + "habituel d'un stage rattrapé à un niveau antérieur : précisez la cohorte d'accueil "
        + "(cohortId) que l'étudiant doit rejoindre.");

    public static Error AlreadyAssignedForStage(int stageId) => Error.Conflict(
        "Revalidations.AlreadyAssigned",
        $"Cette inscription porte déjà une affectation pour le stage {stageId} ; il n'y a rien à "
        + "rouvrir. Consultez le stage existant.");

    public static Error NothingToRevalidate(int stageId) => Error.Conflict(
        "Revalidations.NothingToRevalidate",
        $"L'étudiant n'a jamais passé le stage {stageId} : il relève de la planification normale, "
        + "pas d'une revalidation.");

    public static Error StageAlreadyValidated(int stageId) => Error.Conflict(
        "Revalidations.AlreadyValidated",
        $"Le stage {stageId} est déjà validé lors d'une inscription précédente — un stage acquis "
        + "reste acquis et n'est pas repassé.");

    /// <summary>
    /// A retake is served where the student failed it, so the failed attempt has to say where that was.
    /// It cannot when the original rotation was never recorded — legacy rows with unreadable dates, or
    /// an assignment that was created and never placed.
    /// </summary>
    public static readonly Error OriginalServiceUnknown = Error.Conflict(
        "Revalidations.OriginalServiceUnknown",
        "La rotation initiale n'indique aucun service : impossible de déterminer où repasser le stage. "
        + "Précisez le service d'accueil (serviceId).");

    public static readonly Error IncompletePlacement = Error.Validation(
        "Revalidations.IncompletePlacement",
        "Pour placer la rotation il faut une date de début et une date de fin ; sinon laissez les trois "
        + "champs vides et planifiez la rotation plus tard.");

    public static Error RevalidationStillOpen(int stageId) => Error.Conflict(
        "Revalidations.StillOpen",
        $"Le stage {stageId} n'est pas encore soldé sur une inscription précédente : attendez son "
        + "verdict avant d'ouvrir une revalidation.");
}
