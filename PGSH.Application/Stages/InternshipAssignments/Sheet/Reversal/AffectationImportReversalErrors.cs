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
    public static Error HasChanged(int count) => Error.Conflict(
        "AffectationImportReversal.HasChanged",
        $"{count} affectation(s) ont changé depuis l'import — évaluées, pointées ou replanifiées. "
        + "L'annulation ne peut pas les défaire sans détruire ce qui a été fait depuis ; "
        + "traitez-les à la main, puis relancez l'aperçu.");
}
