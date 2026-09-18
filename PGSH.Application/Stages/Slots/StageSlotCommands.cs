using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Slots;

/// <remarks>
/// <para>⚠ <b>Les six actes de cette famille sont audités, y compris ceux qui construisent.</b> Ils
/// écrivent et défont l'axe et la grille d'une promotion entière, et ce sont eux que la campagne de
/// répartition rejoue — poser, corriger, vider, recommencer. « Qui a posé cet axe » est exactement
/// la question à laquelle il fallait pouvoir répondre pour les rosters, et c'est pourquoi
/// <c>AutoArrangeGroupsCommand</c> est audité : la même raison vaut ici.</para>
///
/// <para>⚠ <b>Chacun porte son propre code.</b> Un code partagé fusionnerait deux actes dans le
/// registre sans que rien ne le signale — voir <c>AuditLogVocabularyTests</c>.</para>
/// </remarks>
public sealed record CreateStageSlotCommand(
    int      StageId,
    int      AcademicYearId,
    int      PeriodNumber,
    string?  Label,
    DateOnly StartDate,
    DateOnly EndDate) : ICommand<int>, IAuditableCommand
{
    public string AuditAction => "STAGE_SLOT_CREATED";
    public string AuditEntityType => "Stage";
    public string? AuditEntityId => StageId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("academicYearId", AcademicYearId),
        ("periodNumber", PeriodNumber),
        ("startDate", StartDate.ToString("yyyy-MM-dd")),
        ("endDate", EndDate.ToString("yyyy-MM-dd")));
}

/// <remarks>
/// ⚠ <b>Déplacer un créneau est l'acte qui décale une promotion entière</b>, et les dates d'avant ne
/// survivent nulle part une fois la ligne écrasée : le handler les dépose dans l'entrée par
/// <c>IAuditTrail</c>. Sans elles, le registre dirait où le créneau est allé sans dire d'où.
/// </remarks>
/// <param name="ConfirmedPeriodCount">
/// Combien de périodes publiées l'opérateur a vu l'aperçu annoncer. ⚠ <b>Obligatoire dès qu'il y en
/// a une</b>, et comparé à ce que l'acte trouve : déplacer une colonne publiée réécrit des milliers
/// de lignes que personne n'a nommées une par une, et une case à cocher ne peut pas attraper le cas
/// qui compte — une période évaluée entre l'aperçu et l'application change ce qui est refusé sans
/// rien changer à l'écran. Omis sur une colonne non publiée, où il n'y a rien à confirmer.
/// </param>
public sealed record UpdateStageSlotCommand(
    int      SlotId,
    int      StageId,
    string?  Label,
    DateOnly StartDate,
    DateOnly EndDate,
    int?     ConfirmedPeriodCount = null) : ICommand<StageSlotMoveResult>, IAuditableCommand
{
    public string AuditAction => "STAGE_SLOT_UPDATED";
    public string AuditEntityType => "StageSlot";
    public string? AuditEntityId => SlotId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("stageId", StageId),
        ("toStartDate", StartDate.ToString("yyyy-MM-dd")),
        ("toEndDate", EndDate.ToString("yyyy-MM-dd")));
}

/// <param name="PeriodsShifted">
/// Périodes publiées dont la fenêtre a effectivement bougé. ⚠ Zéro n'est pas « rien ne s'est passé » :
/// une colonne non publiée en a zéro, et le milieu d'un séjour en service unique aussi — le séjour
/// garde sa durée. <paramref name="PeriodsCovered"/> sépare les deux.
/// </param>
/// <param name="PeriodsCovered">Périodes que la colonne touche, qu'elles aient bougé ou non.</param>
public sealed record StageSlotMoveResult(int PeriodsShifted, int PeriodsCovered);

public sealed record DeleteStageSlotCommand(int SlotId) : ICommand, IAuditableCommand
{
    public string AuditAction => "STAGE_SLOT_DELETED";
    public string AuditEntityType => "StageSlot";
    public string? AuditEntityId => SlotId.ToString();

    /// <summary>Ce que le créneau portait est écrit par le handler : lui seul l'a lu.</summary>
    public string? AuditMetadata => null;
}

/// <remarks>
/// ⚠ Cet acte <b>épingle</b> la cellule (<c>CellSource.Pinned</c>) : c'est une décision humaine que
/// la répartition automatique n'a plus le droit de réécrire. Le registre doit donc pouvoir dire qui
/// l'a prise — c'est la moitié « qui » de ce que <c>PinnedCellsKept</c> compte.
/// </remarks>
public sealed record SetCohortSlotAssignmentCommand(
    int CohortId,
    int StageSlotId,
    int ServiceId) : ICommand<int>, IAuditableCommand
{
    public string AuditAction => "COHORT_SLOT_PINNED";
    public string AuditEntityType => "Cohort";
    public string? AuditEntityId => CohortId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(
        ("stageSlotId", StageSlotId),
        ("serviceId", ServiceId));
}

public sealed record ClearCohortSlotAssignmentCommand(
    int CohortId,
    int StageSlotId) : ICommand, IAuditableCommand
{
    public string AuditAction => "COHORT_SLOT_CLEARED";
    public string AuditEntityType => "Cohort";
    public string? AuditEntityId => CohortId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(("stageSlotId", StageSlotId));
}

public sealed record ClearSlotAssignmentsCommand(int StageSlotId)
    : ICommand<ClearSlotResult>, IAuditableCommand
{
    public string AuditAction => "STAGE_SLOT_CELLS_CLEARED";
    public string AuditEntityType => "StageSlot";
    public string? AuditEntityId => StageSlotId.ToString();

    /// <summary>Combien sont parties et combien ont tenu : le handler, après coup.</summary>
    public string? AuditMetadata => null;
}

/// <param name="Cleared">Cellules supprimées.</param>
/// <param name="Skipped">
/// Cellules laissées parce qu'une période publiée les couvre. ⚠ Un <c>Cleared</c> à zéro a deux
/// causes opposées — colonne déjà vide, ou colonne entièrement publiée — et c'est ce compte qui les
/// sépare.
/// </param>
public sealed record ClearSlotResult(int Cleared, int Skipped);
