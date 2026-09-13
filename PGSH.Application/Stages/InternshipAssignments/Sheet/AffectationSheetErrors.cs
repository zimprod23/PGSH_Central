using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Sheet;

public static class AffectationSheetErrors
{
    public static readonly Error NotAllowed = Error.Forbidden(
        "AffectationSheet.NotAllowed",
        "Le téléversement des affectations est réservé à la scolarité.");

    /// <summary>
    /// A workbook we cannot open is the user having picked the wrong file, not a fault. ⚠ Typed
    /// <c>Validation</c> rather than <c>Problem</c>: above 500 the client discards <c>detail</c> and
    /// shows the fixed « Une erreur serveur est survenue », which is the one sentence that would have
    /// told him to pick another file.
    /// </summary>
    public static readonly Error SheetUnreadable = Error.Validation(
        "AffectationSheet.Unreadable",
        "Le fichier n'a pas pu être lu. Téléversez le canevas .xlsx tel qu'il a été téléchargé.");

    public static readonly Error SheetEmpty = Error.Validation(
        "AffectationSheet.Empty",
        "Le fichier ne contient aucune ligne.");

    /// <summary>
    /// ⚠ The count is the operator's, never re-derived, and it counts <b>affectations</b> rather than
    /// lines: a rotation is several lines and the number on screen has to be the number he is
    /// authorising. The act lands on students nobody typed the name of, and an affectation created,
    /// transferred or evaluated between the aperçu and the apply changes what it does without changing
    /// anything he saw. A boolean « oui j'ai vérifié » cannot catch that; the number can.
    /// </summary>
    public static Error CountMismatch(int confirmed, int actual) => Error.Conflict(
        "AffectationSheet.CountMismatch",
        $"Le fichier ne produit plus le même plan qu'à l'aperçu : {actual} affectation(s) seraient "
        + $"écrites, {confirmed} confirmée(s). Relancez l'aperçu avant d'appliquer.");

    /// <summary>
    /// ⚠ Destruction is confirmed <b>separately</b> from creation. The two numbers move for different
    /// reasons — a période évaluée between the aperçu and the apply changes what is dropped without
    /// changing what is written — and it is the dropped one nothing puts back.
    /// </summary>
    public static Error DroppedMismatch(int confirmed, int actual) => Error.Conflict(
        "AffectationSheet.DroppedMismatch",
        $"Ce que le fichier détruirait a changé depuis l'aperçu : {actual} période(s) seraient "
        + $"supprimées, {confirmed} confirmée(s). Relancez l'aperçu avant d'appliquer.");

    /// <summary>
    /// The file carries a mistake, so nothing is written. The count is on the error rather than only
    /// in the report because a refusal the client shows as a toast must still say how much to look at.
    /// </summary>
    public static Error HasErrors(int count) => Error.Conflict(
        "AffectationSheet.HasErrors",
        $"{count} ligne(s) du fichier ne peuvent pas être appliquées. "
        + "Corrigez-les — l'aperçu les nomme une par une — puis téléversez de nouveau.");

    public static Error LevelNotFound(int levelId) => Error.NotFound(
        "AffectationSheet.LevelNotFound",
        $"Le niveau {levelId} est introuvable.");

    /// <summary>
    /// ⚠ An empty canvas is worse than a refusal: it looks like a promotion with nobody in it, when in
    /// fact the year picker is on a year that promotion did not run.
    /// </summary>
    public static Error PromotionHasNoStudents(string levelLabel, string yearLabel) => Error.Conflict(
        "AffectationSheet.PromotionHasNoStudents",
        $"Aucune inscription en {levelLabel} pour {yearLabel} : il n'y a pas de canevas à produire.");
}
