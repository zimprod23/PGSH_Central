using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization.Bulk;

public static class BulkDelocalizationErrors
{
    public static readonly Error NotAllowed = Error.Forbidden(
        "BulkDelocalization.NotAllowed",
        "La délocalisation en masse est réservée à la scolarité.");

    /// <summary>
    /// ⚠ The count is the operator's, never re-derived. This act writes onto students nobody typed
    /// the name of — a whole roster is named by one id — so a student registered, transferred into
    /// the group or evaluated between the preview and the apply changes what the act does without
    /// changing anything the operator saw. A boolean « oui j'ai vérifié » cannot catch that; the
    /// number can, and the refusal names both so the operator knows what moved.
    /// </summary>
    public static Error CountMismatch(int confirmed, int actual) => Error.Conflict(
        "BulkDelocalization.CountMismatch",
        $"La liste a changé depuis l'aperçu : {actual} étudiant(s) seraient délocalisés, "
        + $"{confirmed} confirmé(s). Relancez l'aperçu avant d'appliquer.");
}
