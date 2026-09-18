namespace PGSH.Domain.Stages;

/// <summary>
/// Qui a décidé des dates d'une colonne de l'axe — la pose de l'axe, ou un humain.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Le pendant de <see cref="CellSource"/> d'un cran plus haut, et il manquait.</b>
/// <c>CohortSlotAssignment.Source</c> empêche l'arrangeur de réécrire une <i>cellule</i> qu'un humain
/// a choisie ; rien n'empêchait un recalcul d'axe de réécrire les <i>dates</i> qu'un humain a
/// choisies. C'est la même faute, atteinte par l'autre bout : une colonne déplacée à la main
/// (phase 17.1) est une décision, souvent prise pour une raison que la grille ne connaît pas — un
/// service fermé cette semaine-là, un jury déplacé — et un axe reposé par-dessus l'effacerait sans
/// refus, sans compte, et avec un résultat parfaitement normal.</para>
///
/// <para>⚠ <b>Une colonne <see cref="MovedByHand"/> n'est pas « sautée », elle <i>ancre</i>.</b> La
/// différence avec une cellule épinglée tient à ce qu'un axe est <b>ordonné</b> : laisser une cellule
/// tranquille ne dérange pas ses voisines, laisser une colonne tranquille contraint les siennes. La
/// cascade reprend donc <i>après</i> elle, et un chevauchement qui en résulterait est un refus nommé
/// plutôt qu'un ordre silencieusement cassé.</para>
///
/// <para><see cref="Laid"/> vaut <c>0</c> pour la même raison que <c>CellSource.Arranged</c> : toute
/// colonne écrite avant l'existence de cette colonne garde le sens qu'elle avait. Aucune ligne de la
/// base n'est donc reclassée par la migration.</para>
/// </remarks>
public enum SlotSource
{
    /// <summary>
    /// Posée par l'axe — <c>ApplyRotationCycleCommand</c>, ou un recalcul. La valeur par défaut, donc
    /// celle de toutes les colonnes existantes.
    /// </summary>
    Laid = 0,

    /// <summary>
    /// Déplacée à la main via <c>UpdateStageSlotCommand</c>. Un recalcul la laisse où elle est.
    /// </summary>
    MovedByHand = 1,
}
