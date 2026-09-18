using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public static class StageErrors
{
    // === Stage ===
    public static Error NotFound(int stageId) => Error.NotFound(
        "Stages.NotFound",
        $"The stage with Id = '{stageId}' was not found.");

    /// <summary>
    /// Le stage ne peut pas partir : quelque chose le nomme encore.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Sans cette garde, la contrainte parlait à la place du refus.</b> <c>Cohorts</c> et
    /// <c>CurriculumStages</c> sont <c>RESTRICT</c>, et <c>ObjectiveScores</c> l'est un cran plus bas
    /// via <c>StageObjectives</c> — donc supprimer un stage rattaché à un CNPN remontait en
    /// <c>DbUpdateException</c> → <b>500</b>, avec pour seule explication le nom d'une contrainte
    /// PostgreSQL. Mesuré à l'usage le 11/09/2026 : l'opérateur a dû deviner qu'il fallait d'abord
    /// retirer le stage du texte.</para>
    ///
    /// <para>⚠ <b>Les raisons sont comptées ensemble, jamais court-circuitées à la première.</b> Un
    /// utilisateur qui retire le stage de son texte pour s'entendre dire ensuite qu'il porte des
    /// cohortes a fait le tour deux fois. Même règle et même forme que
    /// <c>AcademicYearErrors.StillInUse</c>.</para>
    /// </remarks>
    public static Error StillInUse(string stageName, IReadOnlyList<string> holdings) => Error.Conflict(
        "Stages.StillInUse",
        $"« {stageName} » ne peut pas être supprimé : il est encore référencé par "
        + string.Join(", ", holdings)
        + ". Retirez ces rattachements d'abord.");

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

    /// <summary>
    /// L'objectif nommé par la requête n'est pas du stage de cette période : c'est la demande qui est
    /// mal formée, pas le serveur qui est en panne.
    /// </summary>
    public static Error ObjectiveNotInStage(int objectiveId) => Error.Validation(
        "ServiceEvaluations.ObjectiveNotInStage",
        $"L'objectif '{objectiveId}' n'appartient pas au stage de cette période.");

    // === Evaluation import ===
    /// <summary>La feuille nomme une période que le stage n'a pas cette année-là.</summary>
    public static Error ImportPeriodNotInStage(int periodNumber, int stageId) => Error.Validation(
        "ServiceEvaluations.ImportPeriodNotInStage",
        $"Le stage '{stageId}' n'a pas de période P{periodNumber} pour l'année sélectionnée.");

    /// <summary>
    /// The stage ran in other years but not the one being imported. Distinguished from an empty stage
    /// because the fix differs: switch the year in the navbar, rather than plan the stage.
    /// </summary>
    public static Error ImportYearHasNoStudents(int stageId, string yearLabel) => Error.Conflict(
        "ServiceEvaluations.ImportYearHasNoStudents",
        $"Aucun étudiant n'est affecté au stage '{stageId}' pour l'année {yearLabel}.");

    public static readonly Error ImportModeNotSupported = Error.Conflict(
        "ServiceEvaluations.ImportModeNotSupported",
        "L'import ne gère que la note chiffrée et la validation globale : la validation par objectif "
        + "demande une note par objectif, qui ne tient pas dans une ligne de tableur.");

    public static Error ImportRejected(int errorCount) => Error.Conflict(
        "ServiceEvaluations.ImportRejected",
        $"{errorCount} ligne(s) en erreur — aucune note n'a été enregistrée. "
        + "Un import de notes est appliqué en totalité ou pas du tout.");

    /// <summary>Le fichier déposé n'est pas un classeur lisible — un refus sur l'entrée, pas une panne.</summary>
    public static readonly Error ImportSheetUnreadable = Error.Validation(
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
    /// <remarks>
    /// ⚠ <b><c>Conflict</c>, et c'est le plus important des dix-huit.</b> <c>AcademicYearResolver</c>
    /// est le chemin de <i>tout</i> handler qui omet l'année, donc une base sans année courante —
    /// l'état que <c>CurrentYearDesignation</c> laisse si elle est interrompue entre ses deux
    /// écritures — rendait <b>chaque écran</b> en 500. Or <c>errorMiddleware</c> jette <c>detail</c>
    /// au-dessus de 500 et affiche « Une erreur serveur est survenue » : la seule phrase qui dit quoi
    /// faire n'atteignait jamais personne.
    /// </remarks>
    public static readonly Error NoCurrentAcademicYear = Error.Conflict(
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
    /// <remarks>
    /// ⚠ <b>The only capacity-family refusal <c>AllowOverCapacity</c> does not waive</b>, and the
    /// message says so, because the checkbox is right there and its label promises otherwise. Being
    /// full is a target; not being admitted is a fact about the service. The remedy is to change the
    /// plan or to author a quota — the message names both, since a refusal with no way forward is
    /// just a wall.
    /// </remarks>
    public static Error LevelNotAdmitted(
        int periodNumber, string serviceName, string levelLabel,
        DateOnly start, DateOnly end) => Error.Conflict(
        "Schedule.LevelNotAdmitted",
        $"La période {periodNumber} ne peut pas être publiée : le service « {serviceName} » "
        + $"({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) n'accueille pas les étudiants de {levelLabel}. "
        + "Ce refus ne peut pas être forcé : « autoriser le dépassement » ne lève que les dépassements "
        + "d'effectif, pas une promotion que le service n'accueille pas. "
        + "Choisissez un autre service, ou ajoutez un quota pour cette promotion depuis la fiche du service.");

    /// <summary>
    /// Over the number on a service whose chef has refused « autoriser le dépassement d'effectif »
    /// (<c>Service.AllowsOverCapacity</c> is false). Same arithmetic as
    /// <see cref="CapacityExceeded"/> / <see cref="LevelCapacityExceeded"/>, opposite verdict: the
    /// checkbox does not reach this one.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Its own code rather than a sentence appended to the other two</b>, for the reason
    /// <see cref="LevelNotAdmitted"/> has one: what a reader needs first is not how far over the cell
    /// is but that the control on screen will not move it. A message ending on « cochez « autoriser
    /// le dépassement » » sends the admin round a loop the service has already closed, and that is
    /// how a screen gets blamed for a refusal it is reporting correctly.
    /// <para><paramref name="levelLabel"/> is the promotion when the ceiling in force is that
    /// promotion's quota, and null when it is the service's total — the remedy differs, exactly as it
    /// does between the two waivable errors.</para>
    /// </remarks>
    public static Error OverCapacityRefusedByService(
        int periodNumber, string serviceName, string? levelLabel,
        DateOnly start, DateOnly end, int occupancy, int capacity) => Error.Conflict(
        "Schedule.OverCapacityRefusedByService",
        $"La période {periodNumber} ne peut pas être publiée : le service « {serviceName} » "
        + $"({start:dd/MM/yyyy} – {end:dd/MM/yyyy}) accueillerait {occupancy} étudiant(s)"
        + (levelLabel is null
            ? $" pour une capacité de {capacity}. "
            : $" de {levelLabel} pour un quota de {capacity}. ")
        + "Ce service n'autorise pas le dépassement d'effectif : la case « autoriser le dépassement » "
        + "ne lève pas ce refus. Retirez des groupes de ce service"
        + (levelLabel is null
            ? ", ou augmentez sa capacité"
            : ", ou augmentez son quota pour cette promotion")
        + " — ou autorisez le dépassement depuis la fiche du service, en accord avec son chef.");

    /// <summary>
    /// One publish, several cells refused at once — the ordinary shape of a stage-wide publication
    /// on a base that is structurally over-subscribed.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Why an aggregate exists at all.</b> Refusing on the first breach is correct for a single
    /// cohorte and useless for a promotion: the admin raises one quota, waits for the whole publish
    /// again, and is told about the next one. It was worse than that in practice — the stage page
    /// published cohorte by cohorte, so one press of « Publier tout » produced a refusal per cohorte,
    /// dozens of red toasts each naming a different service and none of them naming the scale of the
    /// problem. One refusal, an exact count, and the heaviest few named.
    /// <para><paramref name="notAdmittedCount"/> and <paramref name="refusedOverrideCount"/> are
    /// stated separately because <b>neither half can be forced</b> and the two are fixed in different
    /// places: one is a promotion the service does not take, the other a service that does not accept
    /// being over its number. What is left over is the waivable remainder — and only when there is
    /// one does the sentence offer the checkbox, because offering it against refusals it cannot lift
    /// is what teaches an admin that the screen is lying to them.</para>
    /// </remarks>
    public static Error PublishRefusedByIntake(
        int refusedCells, int notAdmittedCount, int refusedOverrideCount,
        IReadOnlyList<string> worst) => Error.Conflict(
        "Schedule.PublishRefusedByIntake",
        $"La publication est refusée : {refusedCells} affectation(s) dépassent ce que le service accepte"
        + UnforceableClause(notAdmittedCount, refusedOverrideCount)
        + $"Les plus lourdes : {string.Join(" · ", worst)}"
        + (refusedCells > worst.Count ? $" (+{refusedCells - worst.Count} autre(s))" : "")
        + ". Ouvrez la grille de planning pour les corriger"
        + (notAdmittedCount + refusedOverrideCount < refusedCells
            ? ", ou cochez « autoriser le dépassement d'effectif » pour publier malgré les effectifs."
            : "."));

    /// <summary>
    /// Names the halves the checkbox cannot lift, in one clause — or closes the sentence when there
    /// are none. Written out rather than nested in the interpolation above: three combinations of two
    /// counts is exactly where a chain of ternaries stops being readable.
    /// </summary>
    private static string UnforceableClause(int notAdmittedCount, int refusedOverrideCount)
    {
        var halves = new List<string>(2);

        if (notAdmittedCount > 0)
            halves.Add($"{notAdmittedCount} sur un service qui n'accueille pas cette promotion");

        if (refusedOverrideCount > 0)
            halves.Add($"{refusedOverrideCount} sur un service qui n'autorise pas le dépassement d'effectif");

        if (halves.Count == 0)
            return ". ";

        // Agreed with the number of cells, not with the number of halves: « dont 1 sur un service… »
        // followed by a plural verb is the kind of wrongness a reader stops on, and this sentence is
        // read at the worst possible moment.
        string verdict = notAdmittedCount + refusedOverrideCount == 1
            ? "ce refus-là ne peut pas être forcé"
            : "ces refus-là ne peuvent pas être forcés";

        return $", dont {string.Join(" et ", halves)} — {verdict}. ";
    }

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

    /// <summary>
    /// Every service the stage allows for this level is held for named rosters, so the rotation has
    /// nothing left to draw from.
    /// </summary>
    /// <remarks>
    /// ⚠ Its own message rather than <see cref="NoServicesAdmitLevel"/>, because the two send the
    /// reader to opposite screens: « aucun ne vous accueille » is a quota to add, « tous sont
    /// réservés » is a placement mode somebody set — and the second, told as the first, has the
    /// operator widening quotas that were never the obstacle. The count is named because the whole
    /// point of the mode is that a withheld place must never disappear in silence.
    /// </remarks>
    public static Error AllServicesReserved(string stageName, string levelLabel, int reservedCount) =>
        Error.Validation(
            "Schedule.AllServicesReserved",
            $"Les {reservedCount} service(s) autorisés pour « {stageName} » et ouverts à {levelLabel} sont "
            + "tous réservés à des groupes nommés : la répartition automatique n'a aucun service où "
            + "placer les autres cohortes. Ouvrez-en un à la rotation, ou autorisez un service de plus "
            + "pour ce stage.");

    /// <summary>
    /// The order sent back is not a permutation of the services the stage actually allows. Named by
    /// cause rather than as one flat "invalid order": a missing service means the page was opened
    /// before somebody else authored one and the fix is to reload, while an unknown one means the
    /// list has since shrunk — opposite acts, and a single sentence would send the user to the wrong
    /// one.
    /// </summary>
    public static Error ServiceOrderNotAPermutation(
        int missingCount, int unknownCount, int duplicateCount)
    {
        var causes = new List<string>();

        if (missingCount > 0)
            causes.Add($"{missingCount} service(s) autorisé(s) n'y figurent pas");
        if (unknownCount > 0)
            causes.Add($"{unknownCount} service(s) n'appartiennent plus à la liste du stage");
        if (duplicateCount > 0)
            causes.Add($"{duplicateCount} service(s) y figurent deux fois");

        return Error.Conflict(
            "Stages.ServiceOrderNotAPermutation",
            "L'ordre envoyé ne correspond pas aux services autorisés du stage : "
            + string.Join(", ", causes)
            + ". Rechargez la fiche du stage et recommencez — l'ordre décide de la prochaine "
            + "répartition automatique, il ne peut pas être enregistré partiellement.");
    }

    /// <summary>
    /// The list of authorised services changed between the moment the order was decided and the
    /// moment it was written. Distinct from <see cref="ServiceOrderNotAPermutation"/> on purpose:
    /// what the caller sent was well-formed when it was checked, and blaming the payload for a
    /// concurrent edit sends the user looking for a mistake that is not in it.
    /// </summary>
    public static Error ServiceOrderIsStale(int expected, int received) => Error.Conflict(
        "Stages.ServiceOrderIsStale",
        $"La liste des services autorisés a changé pendant l'enregistrement : {expected} service(s) "
        + $"sur le stage, {received} dans l'ordre envoyé. Rien n'a été modifié — rechargez la fiche "
        + "du stage et refaites le classement.");

    public static readonly Error ScheduleNotConfigured = Error.Validation(
        "Schedule.NotConfigured",
        "This cohort has no slot assignments configured. Set up the schedule grid before publishing.");

    public static readonly Error ScheduleAlreadyPublished = Error.Conflict(
        "Schedule.AlreadyPublished",
        "This cohort's schedule has already been published. Unpublish it first before making changes.");

    public static readonly Error ScheduleNotPublished = Error.Validation(
        "Schedule.NotPublished",
        "This cohort's schedule has not been published yet. Nothing to unpublish.");

    /// <summary>
    /// ⚠ <b>Moving a published column is worse than deleting one, and it had no guard at all.</b>
    /// Deleting fails loudly; moving succeeds and desynchronises silently — the créneau takes its new
    /// dates and the périodes published from it keep their old ones, so the grid says one thing and
    /// the students' rotations say another, with nothing on either screen to say which is true.
    /// Refused until « déplacer une colonne publiée *et ses périodes* » exists, which is Phase 17.1
    /// and one operation, not two.
    /// </summary>
    public static readonly Error SlotPublishedCannotMove = Error.Conflict(
        "Schedule.SlotPublishedCannotMove",
        "Ce créneau a des cohortes publiées : en changer les dates laisserait leurs périodes aux "
        + "anciennes, et la grille cesserait de dire ce que font les étudiants. Dépubliez-les d'abord.");

    /// <summary>
    /// Phase 17.1's two refusals. ⚠ Both are <c>Conflict</c>, never <c>Problem</c>: the request meets
    /// the state of the base, and a business refusal typed <c>Problem</c> becomes a 500 whose sentence
    /// the client discards.
    /// </summary>
    public static Error SlotPeriodsAlreadyUnderway(int periods, int evaluated, int attendanceDays) =>
        Error.Conflict(
            "Schedule.SlotPeriodsAlreadyUnderway",
            $"{periods} période(s) issues de ce créneau ont déjà commencé ou portent une trace : "
            + $"{evaluated} évaluation(s) et {attendanceDays} journée(s) de présence. Déplacer la "
            + "colonne sous elles ferait mentir le registre — une note et une présence sont des faits "
            + "sur des dates qui ont eu lieu. Déplacez plutôt les colonnes encore à venir.");

    /// <summary>
    /// La garde de l'agrégat, doublant celle de <c>PublishedPeriodShifter.PlanAsync</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Doublée à dessein.</b> Le planificateur écarte déjà ces périodes, donc ce refus ne devrait
    /// jamais s'afficher — mais un agrégat qui fait confiance à son appelant n'a pas d'invariant, il a
    /// une convention. Une note et une journée de présence sont des faits sur des dates qui ont eu
    /// lieu : rien ne doit pouvoir glisser la fenêtre sous elles, quel que soit le chemin.
    /// </remarks>
    /// <summary>
    /// ⚠ Une fenêtre négative n'est pas seulement fausse, elle est <b>silencieuse</b> : chaque calcul
    /// de durée en aval la lit comme un nombre de jours négatif et rend des totaux que rien n'annonce
    /// comme absurdes. Refusée à l'entrée de l'agrégat plutôt que constatée trois écrans plus loin.
    /// </summary>
    /// <summary>
    /// Les trois moitiés de l'identité d'un créneau. ⚠ Elles ne devraient jamais s'afficher : ce sont
    /// des refus adressés au <em>programmeur</em>, pas à l'utilisateur — un appelant qui omet l'un des
    /// trois a écrit un bug, et la seule chose qui comptait était qu'il ne puisse plus le faire en
    /// silence. Elles sont `Validation` et non `Problem` parce qu'une demande malformée reste une
    /// demande malformée, d'où qu'elle vienne.
    /// </summary>
    public static readonly Error SlotNeedsStage = Error.Validation(
        "Schedule.SlotNeedsStage", "Un créneau appartient à un stage.");

    public static readonly Error SlotNeedsAcademicYear = Error.Validation(
        "Schedule.SlotNeedsAcademicYear",
        "Un créneau appartient à une année universitaire : sans elle il vaudrait pour toutes les "
        + "promotions à la fois, avec les dates d'une seule.");

    public static readonly Error SlotNeedsPeriodNumber = Error.Validation(
        "Schedule.SlotNeedsPeriodNumber", "Un créneau porte un numéro de période (P1, P2…).");

    /// <summary>
    /// Les deux moitiés de l'identité d'une cohorte, refusées pour la même raison et avec la même
    /// portée que celles d'un créneau juste au-dessus : ce sont des refus adressés au
    /// <em>programmeur</em>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Rien dans le schéma ne tient <c>(StageId, AcademicGroupId)</c>.</b> Il n'y a pas d'index
    /// unique derrière cette paire — <c>CreateCohortCommandHandler</c> et <c>CohortProvisioner</c>
    /// cherchent le doublon eux-mêmes — donc ces deux refus ne couvrent que l'autre moitié : une
    /// cohorte sans stage ou sans groupe, c'est-à-dire une ligne qu'aucune lecture ne sait
    /// interpréter. Voir <see cref="Cohort.For(int, int, string)"/>.
    /// </remarks>
    public static readonly Error CohortNeedsStage = Error.Validation(
        "Cohorts.CohortNeedsStage",
        "Une cohorte est un groupe qui fait un stage : sans le stage, il ne reste que le groupe.");

    public static readonly Error CohortNeedsRoster = Error.Validation(
        "Cohorts.CohortNeedsRoster",
        "Une cohorte est un groupe qui fait un stage : sans le groupe, il ne reste que le stage.");

    public static Error PeriodWindowReversed(DateOnly startDate, DateOnly endDate) => Error.Validation(
        "Schedule.PeriodWindowReversed",
        $"La fenêtre demandée se termine ({endDate:dd/MM/yyyy}) avant de commencer "
        + $"({startDate:dd/MM/yyyy}).");

    public static Error PeriodCannotBeRescheduled = Error.Conflict(
        "Schedule.PeriodCannotBeRescheduled",
        "Cette rotation a commencé, ou porte déjà une note ou des journées de présence : sa fenêtre "
        + "ne peut plus être déplacée sans faire mentir le registre.");

    /// <summary>
    /// Le pendant de <see cref="PeriodCannotBeRescheduled"/> pour l'autre question.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Les deux phrases nomment des faits différents, à dessein.</b> Un déplacement bute sur le
    /// démarrage et sur les présences ; un allongement ne bute que sur une clôture, une note ou une
    /// interruption. Une phrase partagée dirait « cette rotation a commencé » à propos d'un
    /// allongement, ce qui est vrai et sans rapport avec le refus — et enverrait l'opérateur chercher
    /// une cause qui n'en est pas une.
    /// </remarks>
    public static readonly Error PeriodCannotBeExtended = Error.Conflict(
        "Schedule.PeriodCannotBeExtended",
        "Cette rotation est close, notée, ou a été coupée par un transfert : sa fin est un fait et ne "
        + "peut plus être repoussée. Une rotation simplement commencée, elle, s'allonge.");

    /// <summary>
    /// ⚠ Un raccourcissement déguisé en allongement. Refusé <b>par le nom de l'acte</b> plutôt que par
    /// une garde sur les présences : ramener la fin en arrière laisserait les journées pointées entre
    /// la nouvelle fin et l'ancienne sur des dates que la fenêtre ne couvre plus. Déplacer une fenêtre
    /// est l'autre acte, et il a sa propre garde.
    /// </summary>
    public static Error PeriodExtensionGoesBackwards(DateOnly currentEnd, DateOnly requestedEnd) =>
        Error.Validation(
            "Schedule.PeriodExtensionGoesBackwards",
            $"Allonger une rotation ne peut que repousser sa fin : elle se termine le "
            + $"{currentEnd:dd/MM/yyyy} et la fenêtre demandée s'arrête le {requestedEnd:dd/MM/yyyy}.");

    /// <summary>
    /// ⚠ Le filet de <c>StageSlot.RelayTo</c>. L'atteindre signale une incohérence entre le
    /// planificateur — qui écarte et compte ces colonnes — et ce qu'il a fini par écrire, jamais un
    /// cas d'usage : un opérateur ne demande pas « repose l'axe sauf là où je l'ai corrigé », il
    /// demande un recalcul, et c'est l'acte qui sait quoi épargner.
    /// </summary>
    public static Error SlotMovedByHandCannotBeRelaid(int periodNumber) => Error.Conflict(
        "Schedule.SlotMovedByHandCannotBeRelaid",
        $"La colonne P{periodNumber} a été déplacée à la main : reposer l'axe par-dessus effacerait "
        + "cette décision. Un recalcul la laisse où elle est et enchaîne les suivantes après elle.");

    public static Error SlotMoveBreaksRun(int fromPeriodNumber, int toPeriodNumber) =>
        Error.Conflict(
            "Schedule.SlotMoveBreaksRun",
            $"Ce déplacement ferait se chevaucher ou s'inverser les périodes P{fromPeriodNumber} et "
            + $"P{toPeriodNumber} d'un même séjour : un stage en service unique est une présence "
            + "continue, et ses colonnes doivent se suivre. Choisissez une fenêtre qui reste entre "
            + "les colonnes voisines.");

    /// <summary>
    /// ⚠ Distinct de <see cref="SlotMoveCountMismatch"/>, et c'est la règle de la maison : « je n'ai
    /// rien confirmé » et « j'ai confirmé autre chose » appellent des gestes différents. Un client qui
    /// n'a jamais ouvert d'aperçu — l'ancien bouton de la grille, qui ne connaît pas encore ce
    /// paramètre — recevrait sinon une phrase lui parlant d'un aperçu qu'il n'a pas vu.
    /// </summary>
    public static Error SlotMoveNotConfirmed(int periods) =>
        Error.Conflict(
            "Schedule.SlotMoveNotConfirmed",
            $"Ce créneau est publié : le déplacer réécrirait la fenêtre de {periods} période(s) déjà "
            + "publiées. Demandez l'aperçu du déplacement, puis renvoyez ce nombre pour confirmer.");

    public static Error SlotMoveCountMismatch(int confirmed, int actual) =>
        Error.Conflict(
            "Schedule.SlotMoveCountMismatch",
            $"Vous avez confirmé le déplacement de {confirmed} période(s), mais {actual} sont "
            + "concernées maintenant. Quelque chose a changé depuis l'aperçu — rouvrez-le et vérifiez "
            + "avant d'appliquer.");

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
    /// Nothing has crossed over into this stage yet, so an unscoped arrange would give every roster
    /// every column of it — the whole promotion in one stage for the whole year, written silently,
    /// after which every stage arranged next gets nothing because everyone is busy everywhere.
    ///
    /// <para>The counterpart of <see cref="SingleServiceRunNotScoped"/> for a per-période stage. Which
    /// partition takes which columns is the crossover, and the crossover is authored — by the rotation
    /// block or the macro matrix — never inferred from an empty grid.</para>
    /// </summary>
    public static Error StageWouldFillEveryColumn(string stageName, int slotCount) => Error.Validation(
        "Schedule.StageWouldFillEveryColumn",
        $"Aucun autre stage n'occupe encore ces groupes : répartir « {stageName} » sans préciser de période "
        + $"placerait chaque groupe dans ses {slotCount} périodes, soit la promotion entière dans ce seul "
        + "stage toute l'année — et les stages répartis ensuite n'obtiendraient plus aucun créneau. "
        + "Établissez d'abord le croisement (bloc de rotation, ou plan macro), puis répartissez.");

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
        $"La rotation '{periodId}' n'a pas été démarrée : elle ne peut pas être clôturée.");

    /// <remarks>
    /// ⚠ <b>Le remède que cette phrase nomme n'est plus un acte de l'application.</b> L'acte
    /// « reprendre » a été retiré le 18/09/2026 avec la pause par étape, et rien ne peut plus poser
    /// <c>IsPaused</c> : une période suspendue en base ne peut venir que d'une annulation d'import qui
    /// a remis le drapeau tel quel (<c>RestoredPeriod</c>). Dire « reprenez-la » enverrait donc
    /// chercher un bouton absent — la phrase nomme ce qui est réellement possible.
    /// </remarks>
    public static Error PeriodPaused(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.Paused",
        $"La rotation '{periodId}' est enregistrée comme suspendue et ne peut pas être clôturée en "
        + "l'état. Corrigez ses dates par « Déplacer la colonne », ou reprenez l'affectation depuis "
        + "le dossier de l'étudiant.");

    public static Error PeriodInterrupted(Guid periodId) => Error.Conflict(
        "AssignmentPeriods.Interrupted",
        $"Service period '{periodId}' was interrupted by a mid-stage transfer; it is terminal history and cannot be started, closed or evaluated.");

    // === Canevas des affectations ===
    // ⚠ Le même refus que la délocalisation, et pour la même raison : une note est la seule chose
    // ici que rien ne remet. L'import refuse la ligne plus tôt et plus utilement ; ceci est la garde
    // qui tient quand la note est saisie entre l'aperçu et l'application.
    public static readonly Error DeclaredRotationOverMark = Error.Conflict(
        "Affectations.DeclaredRotationOverMark",
        "Ce stage porte déjà une évaluation : la réécrire supprimerait la note enregistrée. "
        + "Corrigez ou supprimez l'évaluation d'abord.");

    // ⚠ La seconde chose que rien ne remet, et elle manquait jusqu'au 13/09/2026. `AttendanceRecord`
    // cascade depuis `ServicePeriod` : réécrire une rotation commencée supprimait donc en silence les
    // journées de présence qu'une secrétaire a saisies une par une. Une note s'annonce sur tous les
    // écrans ; une présence est invisible jusqu'au jour où on en a besoin.
    public static readonly Error DeclaredRotationOverAttendance = Error.Conflict(
        "Affectations.DeclaredRotationOverAttendance",
        "Ce stage porte des journées de présence : les réécrire les supprimerait. "
        + "Le fichier peut replanifier un stage qui n'a pas commencé, pas réécrire ce qui a eu lieu.");

    // ⚠ Les mêmes deux refus sur le chemin inverse, mais pour une autre raison : ici, ni la note ni la
    // présence ne peuvent venir de l'import — il a refusé d'y toucher à l'aller. Elles sont arrivées
    // *depuis*, donc l'affectation n'est plus celle que l'import a écrite, et la défaire détruirait un
    // travail que ce registre ne sait pas remettre.
    public static readonly Error RestoredRotationOverMark = Error.Conflict(
        "Affectations.RestoredRotationOverMark",
        "Ce stage a été évalué depuis l'import : annuler l'import supprimerait la note. "
        + "Corrigez l'affectation à la main, ou supprimez l'évaluation d'abord.");

    public static readonly Error RestoredRotationOverAttendance = Error.Conflict(
        "Affectations.RestoredRotationOverAttendance",
        "Des journées de présence ont été saisies depuis l'import : annuler l'import les supprimerait.");

    public static Error AffectationImportNotFound(Guid id) => Error.NotFound(
        "Affectations.ImportNotFound",
        $"Aucun import d'affectations {id}.");

    public static Error AffectationImportAlreadyReversed(Guid id) => Error.Conflict(
        "Affectations.ImportAlreadyReversed",
        "Cet import a déjà été annulé.");

    // ⚠ Pas « ne fait rien » : une déclaration vide voudrait dire « ce stage n'a pas de périodes »,
    // ce qui n'est pas un plan mais une affectation amputée — et elle serait indiscernable d'un
    // fichier dont les lignes se sont perdues en chemin.
    public static readonly Error DeclaredRotationEmpty = Error.Validation(
        "Affectations.DeclaredRotationEmpty",
        "Une affectation déclarée porte au moins une période.");

    // === Delocalization ===
    // ⚠ Replaces Delocalizations.StageAlreadyUnderway, which refused as soon as a period had begun.
    // A student who leaves for an external hospital mid-rotation is the ordinary case, not the
    // suspicious one; what may never be overwritten is a mark somebody gave.
    public static readonly Error DelocalizationOverMark = Error.Conflict(
        "Delocalizations.OverMark",
        "Ce stage porte déjà une évaluation : la délocalisation supprimerait la note enregistrée. "
        + "Corrigez ou supprimez l'évaluation d'abord si le stage a bien été effectué hors faculté.");

    public static readonly Error NotDelocalized = Error.Conflict(
        "Delocalizations.NotDelocalized",
        "Ce stage n'est pas délocalisé ; il n'y a rien à annuler.");

    public static readonly Error DelocalizationAlreadyMarked = Error.Conflict(
        "Delocalizations.AlreadyMarked",
        "La validation rapportée par l'étudiant est déjà enregistrée : annuler la délocalisation "
        + "supprimerait la seule trace du stage. Corrigez l'évaluation si elle est erronée.");

    public static readonly Error ExternalServiceNotAllowedInStage = Error.Conflict(
        "Stages.ExternalServiceNotAllowed",
        "Ce service est hors faculté : il ne peut pas figurer dans la rotation d'un stage. "
        + "Les étudiants y sont placés par une délocalisation, jamais par la répartition.");

    /// <summary>
    /// The service exists but this stage does not authorise it, so there is no authorisation row to
    /// carry a placement mode. ⚠ Distinct from <see cref="ServiceErrors.NotFound"/>: « le service
    /// n'existe pas » and « ce stage ne l'autorise pas » are one typo and one missing act.
    /// </summary>
    public static Error ServiceNotAllowedInStage(int serviceId, int stageId) => Error.Conflict(
        "Stages.ServiceNotAllowed",
        $"Le service {serviceId} ne fait pas partie des services autorisés du stage {stageId} : "
        + "autorisez-le d'abord, puis choisissez comment il est pourvu.");

    public static readonly Error NoGroupForDelocalization = Error.Conflict(
        "Delocalizations.NoGroup",
        "L'étudiant n'est rattaché à aucun groupe pour cette année ; affectez-le à un groupe avant de délocaliser le stage.");

    public static Error NoWindowForDelocalization(string stageName, string yearLabel) => Error.Conflict(
        "Delocalizations.NoWindow",
        $"Aucun créneau n'est défini pour « {stageName} » en {yearLabel} : le stage n'a pas de dates "
        + "officielles à reprendre. Indiquez les dates de la délocalisation, ou créez d'abord les "
        + "périodes du stage.");

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

    /// <summary>
    /// Removing a cohorte removes the affectations built on it, and every <c>ServicePeriod</c> hangs
    /// off an affectation — so <c>ServiceEvaluation</c>, <c>AttendanceRecord</c>, <c>PeriodPause</c>
    /// and <c>Delocalization</c> cascade away with them.
    /// </summary>
    /// <remarks>
    /// ⚠ Deliberately not forceable. Unpublishing has a <c>Force</c> because it is the declared
    /// inverse of publishing and its whole subject is the schedule; « supprimer la cohorte » is a
    /// structural act, and nobody reaching for it means "and destroy the marks". Stop the rotation
    /// first — the unpublish path is guarded, states its cost and asks twice — then delete.
    /// </remarks>
    public static Error CohortAffectationsUnderway(
        string cohortLabel, int assignments, int periods,
        int started, int evaluated, int attendanceDays) => Error.Conflict(
        "Cohorts.AffectationsUnderway",
        $"La cohorte « {cohortLabel} » est engagée : {assignments} affectation(s), {periods} période(s) "
        + $"dont {started} démarrée(s), {evaluated} évaluation(s) et {attendanceDays} journée(s) de "
        + "présence. La supprimer effacerait définitivement les évaluations et les présences. Arrêtez "
        + "d'abord la rotation — dépubliez la répartition de cette cohorte, qui indique ce qui serait "
        + "perdu — puis supprimez-la.");

    /// <summary>
    /// The same refusal for « Réinitialiser les cohortes », which reaches every cohorte of one stage
    /// in one year. Same rule, same numbers, one message per act rather than a shared « cannot ».
    /// </summary>
    public static Error StageCohortsUnderway(
        string stageName, string yearLabel, int cohorts, int assignments, int periods,
        int started, int evaluated, int attendanceDays) => Error.Conflict(
        "Cohorts.StageUnderway",
        $"« {stageName} » est engagé en {yearLabel} : {cohorts} cohorte(s), {assignments} affectation(s), "
        + $"{periods} période(s) dont {started} démarrée(s), {evaluated} évaluation(s) et "
        + $"{attendanceDays} journée(s) de présence. Réinitialiser effacerait définitivement les "
        + "évaluations et les présences. Dépubliez d'abord la répartition du stage — cette action "
        + "indique précisément ce qu'elle coûte — puis réinitialisez.");

    /// <summary>
    /// « Changement de groupe » met an affectation that is no longer a plan. The act asserts the
    /// student was <i>always</i> in the target roster, and past <c>Planned</c> that is contradicted by
    /// the record itself — a rotation begun, a mark, a verdict.
    /// </summary>
    /// <remarks>
    /// It names the transfer rather than offering a force: moving a student whose stage has actually
    /// started is what <c>TransferStudentCommand</c> is for, and it keeps the trace precisely because
    /// there is now something to trace.
    /// </remarks>
    public static Error AffectationNotCorrectable(Guid assignmentId, InternshipStatus status) =>
        Error.Conflict(
            "Affectations.NotCorrectable",
            $"L'affectation {assignmentId} n'est plus une simple prévision (état : {status}). Un "
            + "changement de groupe déclare que l'étudiant a toujours été dans le groupe d'arrivée, ce "
            + "que cette rotation contredit. Utilisez un transfert : il fait suivre la rotation en "
            + "cours et en conserve la trace.");
}
