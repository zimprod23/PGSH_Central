namespace PGSH.Application.Stages.Delocalization;

/// <summary>
/// The dates a délocalisation is recorded under, and <b>where they came from</b> — which is not a
/// detail, because the three provenances are not equally precise.
/// </summary>
/// <remarks>
/// <para>The dates are the faculty's, not the external hospital's. What a student actually does once
/// he is in Kénitra follows that hospital's own calendar, and the app has no way to learn it; what
/// PGSH records is the period the stage officially occupies for him, which is what every other read —
/// the dossier, the parcours, the export — is measured against.</para>
///
/// <para>⚠ <b>The provenance travels with the dates because one of the three is wider than a
/// passage.</b> A number standing for two states is the recurring defect in this codebase, and
/// « les dates du groupe » and « les dates de tout le stage » call for different reactions from the
/// operator: the second is a hint that the répartition has not been arranged yet.</para>
/// </remarks>
public readonly record struct DelocalizationWindow(
    DateOnly Start,
    DateOnly End,
    DelocalizationWindowSource Source);

/// <summary>Where a <see cref="DelocalizationWindow"/>'s dates were taken from.</summary>
public enum DelocalizationWindowSource
{
    /// <summary>
    /// Scolarité named them. The external hospital's own calendar, when it happens to be known —
    /// always the most precise answer, and the only one PGSH does not derive.
    /// </summary>
    Named,

    /// <summary>
    /// The cohorte's own passage through the stage, read off the cells it holds in the grid. This is
    /// the answer whenever the répartition has been arranged, and it is the right one.
    /// </summary>
    Cohort,

    /// <summary>
    /// ⚠ <b>The whole stage's axis</b> — every créneau of the promotion, because this cohorte holds no
    /// cell yet and PGSH does not know where it will pass. On a stage crossed by six partitions this
    /// is six times a single passage, so it is reported rather than passed off as a measurement.
    /// </summary>
    StageAxis,
}
