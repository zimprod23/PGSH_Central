namespace PGSH.Application.Stages.Schedule;

/// <summary>
/// Why the planning grid of a stage is empty — because « rien n'est planifié » and « cette année n'a
/// jamais été planifiée ici » call for opposite acts, and a blank table says neither.
///
/// <para><b>Why this exists.</b> Measured 2026-09-04: from 2017-2018 to 2025-2026 the base holds
/// <b>105 626 périodes for 0 créneau and 0 cellule</b> — the Access import carried the rotations that
/// were <i>served</i>, and the source had no planning grid to carry. Only 2026-2027 has one. So
/// opening the grid on any past year shows an empty table while every student dossier shows his
/// périodes, which is the symptom the user reported. Nothing is damaged; what was missing is the
/// <i>sentence</i>, and an admin reading the blank as « rien n'est réparti » can go and lay an axis
/// over a year that finished.</para>
///
/// <para>Same rule as <c>RepartitionSummary.DeclaredSlotCount</c> and <c>ExportNotes</c>: an absence
/// has to announce itself, or it stands in for a defect. ⚠ And it is <b>three</b> causes, not two —
/// the lesson of the placements panel, where a promotion holding no roster at all was told « rien
/// n'est réparti », which points at the second gesture instead of the first.</para>
/// </summary>
public static class StageScheduleNotes
{
    /// <summary>
    /// The grid has no column at all: either this year predates the planning grid, or nobody has laid
    /// an axis yet. <paramref name="servedPeriodCount"/> is what separates them, and they are opposite
    /// acts — the first is finished history, the second is the first gesture of planning.
    /// </summary>
    /// <remarks>
    /// ⚠ It deliberately says the imported rotations are <b>not</b> something an axis would recover.
    /// Laying one over a closed year writes columns nothing will ever be arranged into, and leaves the
    /// grid disagreeing with the dossiers it is supposed to describe.
    /// </remarks>
    public static string NoAxisNote(int servedPeriodCount) => servedPeriodCount == 0
        ? "Aucun créneau n'est posé pour ce stage sur cette année : c'est un axe (bloc de rotation) "
          + "qui les crée, et rien ne peut être réparti avant."
        : $"Aucun créneau n'est posé pour ce stage sur cette année, alors que {servedPeriodCount} "
          + "période(s) y ont été servies : ces rotations viennent de l'historique importé, qui ne "
          + "portait aucune grille de planification. Il n'y a rien à répartir ici — poser un axe sur "
          + "une année déjà servie ne reconstituerait pas ce qui a eu lieu.";

    /// <summary>
    /// The columns are there and no cohorte stands in any of them: either the stage has no cohorte to
    /// place, or it has them and nobody has arranged them yet.
    /// </summary>
    /// <remarks>
    /// ⚠ <paramref name="cohortCount"/> is the <b>stage's</b>, never the filtered selection's. Under a
    /// partition filter an empty answer is the filter's doing, and telling the user to provision
    /// cohortes he already has sends him to undo a cut that is correct.
    /// </remarks>
    public static string NothingArrangedNote(int declaredSlotCount, int cohortCount) => cohortCount == 0
        ? $"{declaredSlotCount} créneau(x) sont posés, mais ce stage n'a aucune cohorte sur cette "
          + "année : la promotion doit être découpée et ses cohortes provisionnées avant toute "
          + "répartition."
        : $"{declaredSlotCount} créneau(x) et {cohortCount} cohorte(s) sont en place, et aucune "
          + "cellule n'est répartie : c'est la répartition automatique qui reste à lancer.";
}
