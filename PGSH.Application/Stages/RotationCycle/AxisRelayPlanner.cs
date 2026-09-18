using PGSH.Domain.Calendar;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>Une colonne de l'axe telle qu'elle est aujourd'hui.</summary>
/// <param name="IsMovedByHand">
/// ⚠ Une colonne qu'un humain a déplacée <b>ancre</b> : elle garde ses dates et la cascade reprend
/// après elle. Voir <c>StageSlot.Source</c>.
/// </param>
internal sealed record AxisColumn(
    int Number, DateOnly StartDate, DateOnly EndDate, bool IsMovedByHand);

/// <param name="Anchored">
/// Vrai quand la colonne n'a pas bougé <em>parce qu'un humain l'avait placée</em> — à distinguer
/// d'une colonne qui n'a pas bougé parce que rien ne la poussait, laquelle porte simplement des
/// dates identiques. Un seul « n'a pas bougé » pour deux causes est le défaut récurrent du dépôt.
/// </param>
internal sealed record RelaidColumn(
    int Number,
    DateOnly FromStart, DateOnly FromEnd,
    DateOnly ToStart, DateOnly ToEnd,
    int FromWorkingDays, int ToWorkingDays,
    bool Anchored)
{
    public bool Moved => FromStart != ToStart || FromEnd != ToEnd;
}

/// <param name="WorkingDaysRecovered">
/// Ce que l'opération rend aux étudiants : la somme, sur les colonnes recalculées, des jours
/// ouvrables regagnés. ⚠ C'est le chiffre qui dit <em>pourquoi</em> jouer l'acte ; « 14 colonnes
/// déplacées » dit seulement son ampleur.
/// </param>
/// <param name="AxisEndsOn">
/// Jusqu'où l'axe court après recalcul. Repousser des colonnes allonge l'année, et une promotion qui
/// finirait en juillet est une décision, pas un détail d'arithmétique.
/// </param>
internal sealed record AxisRelayPlan(
    IReadOnlyList<RelaidColumn> Columns,
    int ColumnsMoved,
    int ColumnsAnchored,
    int WorkingDaysRecovered,
    DateOnly AxisEndsOn)
{
    public static readonly AxisRelayPlan Empty =
        new([], 0, 0, 0, default);
}

/// <summary>
/// Repose les colonnes d'un axe sur le calendrier de sa promotion, à partir de la première colonne
/// touchée — l'arithmétique du rattrapage d'une fenêtre déclarée trop tard.
///
/// <para>Pure : ni base, ni horloge. C'est ce qui permet d'éprouver les cas pénibles — une fenêtre
/// qui coupe la première colonne, une colonne ancrée au milieu, un axe qui déborde — exhaustivement
/// plutôt que par une fixture de planification, exactement comme <see cref="RotationCyclePlanner"/>.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Le modèle.</b> Un axe est <c>T</c> colonnes de <c>n</c> jours ouvrables chacune, posées
/// bout à bout depuis une date d'ancrage (<c>WorkingDayCalendar.LaySeries</c>), et
/// <em>chaque stage du bloc porte une colonne par numéro</em> — « P3 » est donc une date, pas une
/// date par stage. Reposer l'axe, c'est refaire cette pose sur le calendrier <b>courant</b>, celui
/// qui contient désormais la fenêtre déclarée.</para>
///
/// <para>⚠ <b>La première colonne recalculée garde son début.</b> C'est la différence entre cet acte
/// et « reposer l'axe depuis le départ » : la fenêtre tombe au milieu d'une colonne déjà commencée,
/// et les étudiants y sont entrés à cette date-là. On ne déplace pas ce début — on <b>prolonge</b>
/// la colonne du nombre de jours que la fenêtre lui a pris, puis les suivantes s'enchaînent. C'est
/// exactement ce que <c>ServicePeriodLifecycle.Extendable</c> autorise là où <c>Movable</c> refuse.
/// </para>
///
/// <para>⚠ <b>Une colonne déplacée à la main ancre, elle n'est pas sautée.</b> Un axe est
/// <em>ordonné</em> : laisser une cellule tranquille ne dérange pas ses voisines, laisser une colonne
/// tranquille contraint les siennes. La cascade reprend donc après elle, et si la colonne qui la
/// précède venait mordre dessus c'est un <b>refus nommé</b> plutôt qu'un ordre cassé en silence.</para>
///
/// <para>⚠ <b>Rejouable.</b> Toutes les dates sont dérivées du calendrier, jamais ajoutées à ce qui
/// est stocké : reposer deux fois de suite donne le même axe, et révoquer la fenêtre puis reposer
/// rend les dates d'origine sans que rien n'ait eu à être défait. C'est la propriété pour laquelle la
/// pause par étape a été retirée plutôt que réparée.</para>
/// </remarks>
internal static class AxisRelayPlanner
{
    /// <summary>
    /// Ce que deviendraient les colonnes, sans rien écrire.
    /// </summary>
    /// <param name="columns">L'axe tel qu'il est, dans n'importe quel ordre.</param>
    /// <param name="calendar">Le calendrier <b>de la promotion</b> — fenêtres déclarées comprises.</param>
    /// <param name="workingDaysPerColumn">La longueur voulue d'une colonne, en jours ouvrables.</param>
    /// <param name="fromColumn">
    /// La première colonne à recalculer. Les colonnes avant elle ne sont pas touchées : elles sont
    /// derrière nous.
    /// </param>
    public static Result<AxisRelayPlan> Plan(
        IReadOnlyList<AxisColumn> columns,
        WorkingDayCalendar calendar,
        int workingDaysPerColumn,
        int fromColumn)
    {
        if (workingDaysPerColumn < 1)
            return Result.Failure<AxisRelayPlan>(RotationCycleErrors.InvalidPeriods);

        var ordered = columns.OrderBy(c => c.Number).ToList();
        if (ordered.Count == 0)
            return Result.Failure<AxisRelayPlan>(RotationCycleErrors.NoColumnsToRelay);

        var affected = ordered.Where(c => c.Number >= fromColumn).ToList();
        if (affected.Count == 0)
            return Result.Failure<AxisRelayPlan>(RotationCycleErrors.NoColumnsToRelay);

        var relaid = new List<RelaidColumn>(affected.Count);

        // Null tant qu'aucune colonne n'a été posée : la première garde son début, donc il n'y a pas
        // encore de « fin précédente » à enchaîner.
        DateOnly? previousEnd = null;

        foreach (var column in affected)
        {
            if (column.IsMovedByHand)
            {
                if (previousEnd is { } end && end >= column.StartDate)
                    return Result.Failure<AxisRelayPlan>(
                        RotationCycleErrors.RelayOverlapsAnchoredColumn(
                            column.Number, column.StartDate, end));

                relaid.Add(Unchanged(column, calendar, anchored: true));
                previousEnd = column.EndDate;
                continue;
            }

            // La première colonne recalculée garde son début — les étudiants y sont déjà entrés.
            // Les suivantes commencent au premier jour qui peut borner après la précédente.
            var from = previousEnd is { } previous ? previous.AddDays(1) : column.StartDate;

            var window = calendar.Lay(from, workingDaysPerColumn);
            if (window is null)
                return Result.Failure<AxisRelayPlan>(
                    RotationCycleErrors.AxisDoesNotFit(affected.Count, relaid.Count));

            relaid.Add(new RelaidColumn(
                column.Number,
                column.StartDate, column.EndDate,
                window.Start, window.End,
                calendar.Count(column.StartDate, column.EndDate),
                window.WorkingDays,
                Anchored: false));

            previousEnd = window.End;
        }

        return new AxisRelayPlan(
            relaid,
            ColumnsMoved: relaid.Count(c => c.Moved),
            ColumnsAnchored: relaid.Count(c => c.Anchored),
            // ⚠ Sur les colonnes recalculées seulement, et jamais négatif par construction : une
            // colonne reposée tient toujours ses n jours ouvrables, c'est la définition de Lay.
            WorkingDaysRecovered: relaid.Sum(c => c.ToWorkingDays - c.FromWorkingDays),
            AxisEndsOn: relaid[^1].ToEnd);
    }

    /// <summary>
    /// La première colonne que <paramref name="calendar"/> ampute — celle par laquelle un rattrapage
    /// commence quand l'appelant n'en nomme pas.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>« Amputée » se mesure, elle ne se déduit pas d'un chevauchement avec la fenêtre.</b> Une
    /// fenêtre posée sur un week-end ne prend aucun jour ouvrable à personne, et la signaler ferait
    /// proposer un acte qui ne changerait rien — du bruit, et le bruit se fait ignorer.
    /// </remarks>
    public static int? FirstShortColumn(
        IReadOnlyList<AxisColumn> columns, WorkingDayCalendar calendar, int workingDaysPerColumn) =>
        columns
            .OrderBy(c => c.Number)
            .Where(c => !c.IsMovedByHand)
            .Where(c => calendar.Count(c.StartDate, c.EndDate) < workingDaysPerColumn)
            .Select(c => (int?)c.Number)
            .FirstOrDefault();

    private static RelaidColumn Unchanged(AxisColumn column, WorkingDayCalendar calendar, bool anchored)
    {
        int held = calendar.Count(column.StartDate, column.EndDate);

        return new RelaidColumn(
            column.Number,
            column.StartDate, column.EndDate,
            column.StartDate, column.EndDate,
            held, held,
            anchored);
    }
}
