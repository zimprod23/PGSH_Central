using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;

public static class AffectationImportReversalErrors
{
    /// <summary>
    /// ⚠ The count is the operator's, never re-derived — and it counts the <b>destructive</b> half:
    /// affectations the import created, which the undo deletes whole. Restoring a rotation can be
    /// re-done by sending the file again; a deleted affectation cannot be put back by anything else.
    /// </summary>
    public static Error CountMismatch(int confirmed, int actual) => Error.Conflict(
        "AffectationImportReversal.CountMismatch",
        $"Ce que l'annulation supprimerait a changé depuis l'aperçu : {actual} affectation(s) seraient "
        + $"supprimées, {confirmed} confirmée(s). Relancez l'aperçu avant d'annuler.");

    /// <summary>
    /// Something happened since the import, so walking it back would not restore a previous state — it
    /// would impose an old one over somebody's work. All or nothing, like the import itself.
    /// </summary>
    /// <summary>
    /// ⚠ The purge deletes rows nobody named one by one, so it carries the operator's count like every
    /// other bulk act — a student deleted between the list and the purge changes what goes without
    /// changing anything he saw.
    /// </summary>
    public static Error PurgeCountMismatch(int confirmed, int actual) => Error.Conflict(
        "AffectationImportReversal.PurgeCountMismatch",
        $"La liste a changé depuis l'affichage : {actual} import(s) seraient supprimés, "
        + $"{confirmed} confirmé(s). Rechargez la liste avant de purger.");

    /// <summary>⚠ Same reason as the import's: an absent confirmation is not a confirmation of zero.</summary>
    public static readonly Error ConfirmationRequired = Error.Validation(
        "AffectationImportReversal.ConfirmationRequired",
        "Le nombre confirmé vient de l'aperçu de l'annulation : relancez-le et renvoyez-le.");

    public static Error HasChanged(int count) => Error.Conflict(
        "AffectationImportReversal.HasChanged",
        $"{count} affectation(s) ont changé depuis l'import — évaluées, pointées ou replanifiées. "
        + "L'annulation ne peut pas les défaire sans détruire ce qui a été fait depuis ; "
        + "traitez-les à la main, puis relancez l'aperçu.");
}
