namespace PGSH.Application.Stages.RotationCycle;

/// <summary>Ce qu'une colonne devient si l'axe est reposé.</summary>
/// <param name="Anchored">
/// Elle n'a pas bougé <b>parce qu'un humain l'avait placée</b>. ⚠ À distinguer d'une colonne qui n'a
/// pas bougé parce que rien ne la poussait : les deux portent des dates identiques et appellent des
/// gestes différents.
/// </param>
public sealed record AxisRelayColumnResponse(
    int PeriodNumber,
    DateOnly FromStartDate, DateOnly FromEndDate,
    DateOnly ToStartDate, DateOnly ToEndDate,
    int FromWorkingDays, int ToWorkingDays,
    bool Anchored,
    bool Moved);

/// <summary>
/// L'aperçu d'un recalcul d'axe : ce qu'il déplacerait, ce qu'il rendrait, ce qu'il ne peut pas
/// rattraper.
/// </summary>
/// <param name="ColumnLength">
/// La longueur d'une colonne en jours ouvrables, <b>dérivée</b> de l'axe et non demandée. Affichée
/// parce qu'elle décide de tout le reste : un opérateur doit pouvoir voir que le recalcul a compris
/// « 22 jours » avant de le laisser réécrire une promotion.
/// </param>
/// <param name="ColumnsAgreeingOnLength">
/// Combien de colonnes portent réellement cette longueur, sur <paramref name="ColumnCount"/>. ⚠ Une
/// minorité discordante est normale — ce sont les colonnes que la fenêtre ampute. Une majorité
/// discordante veut dire que l'axe n'a pas été posé en jours ouvrables.
/// </param>
/// <param name="PeriodsToMove">Rotations dont la fenêtre entière se déplace : rien n'y a commencé.</param>
/// <param name="PeriodsToExtend">
/// Rotations dont seule la fin recule. Elles ont commencé, et leur début est un fait.
/// </param>
/// <param name="PeriodsBlocked">
/// ⚠ Rotations que l'acte <b>ne rattrapera pas</b> : closes, notées, pointées ou interrompues. Elles
/// n'empêchent pas l'acte — une note est un fait et le reste de la promotion a besoin d'être poussé —
/// mais elles se comptent, sinon « 4 010 rotations déplacées » laisserait croire que tout le monde a
/// été rattrapé.
/// </param>
/// <param name="PeriodsToShorten">
/// Rotations dont la fin <b>revient en arrière</b> : l'axe revient d'une fenêtre révoquée. Comptées à
/// part de <paramref name="PeriodsToExtend"/> parce que ce sont deux directions, et que l'opérateur
/// doit voir laquelle il s'apprête à appliquer. ⚠ La garde n'est pas la même non plus : raccourcir
/// peut orpheliner une journée pointée, allonger ne le peut pas.
/// </param>
/// <param name="WorkingDaysChanged">
/// Ce que l'acte rend aux étudiants — ou leur reprend. ⚠ C'est le chiffre qui dit <em>pourquoi</em>
/// le jouer ; <paramref name="ColumnsMoved"/> n'en dit que l'ampleur.
///
/// <para>⚠ <b>Signé, et les deux signes sont des actes légitimes.</b> Positif : une fenêtre a été
/// déclarée et l'axe rattrape ce qu'elle a pris. Négatif : la fenêtre a été <em>révoquée</em> et
/// l'axe revient où il était. Zéro n'arrive pas — il est refusé en amont, parce que « l'acte n'a
/// rien trouvé à faire » et « l'acte n'a rien fait » sont deux états qu'un zéro confondrait.</para>
/// </param>
/// <param name="AxisEndsOn">
/// Jusqu'où l'année court après recalcul. ⚠ Repousser des colonnes allonge l'année universitaire, et
/// une promotion qui finirait en juillet est une décision — pas un détail d'arithmétique.
/// </param>
public sealed record AxisRelayPreviewResponse(
    int AcademicYearId,
    int LevelId,
    int ColumnLength,
    int ColumnsAgreeingOnLength,
    int ColumnCount,
    int FromPeriodNumber,
    IReadOnlyList<AxisRelayColumnResponse> Columns,
    int ColumnsMoved,
    int ColumnsAnchored,
    int SlotsToRelay,
    int PeriodsToMove,
    int PeriodsToExtend,
    int PeriodsToShorten,
    int PeriodsBlocked,
    int WorkingDaysChanged,
    DateOnly AxisEndsOn,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Le premier des deux nombres à confirmer : ce que l'acte réécrit dans la grille.</summary>
    public int SlotsAffected => SlotsToRelay;

    /// <summary>Le second : ce que l'acte réécrit dans les dossiers des étudiants.</summary>
    public int PeriodsAffected => PeriodsToMove + PeriodsToExtend + PeriodsToShorten;

    /// <summary>
    /// L'axe revient-il en arrière ? Pour que l'écran dise « rattraper » ou « revenir » plutôt que
    /// « recalculer », qui ne dit ni l'un ni l'autre.
    /// </summary>
    public bool IsRollingBack => WorkingDaysChanged < 0;
}

/// <param name="PeriodsBlocked">
/// Ce que l'acte a laissé derrière lui, et pourquoi il n'a pas échoué pour autant.
/// </param>
public sealed record AxisRelayResult(
    int SlotsRelaid,
    int PeriodsMoved,
    int PeriodsExtended,
    int PeriodsShortened,
    int PeriodsBlocked,
    int WorkingDaysChanged,
    DateOnly AxisEndsOn);
