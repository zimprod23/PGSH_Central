using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.ReinscriptionSheet;

public static class ReinscriptionSheetErrors
{
    public static readonly Error NotAllowed = Error.Forbidden(
        "ReinscriptionSheet.NotAllowed",
        "Seule la scolarité peut appliquer un fichier de réinscription.");

    public static readonly Error SheetUnreadable = Error.Validation(
        "ReinscriptionSheet.Unreadable",
        "Le fichier n'a pas pu être lu. Attendu : un classeur .xlsx dont la première feuille porte "
        + "les colonnes Code, NOM, PRENOM, puis les deux colonnes « Etape » (année en cours, puis "
        + "année de destination).");

    public static readonly Error SheetIsEmpty = Error.Validation(
        "ReinscriptionSheet.Empty",
        "Le fichier ne contient aucune ligne exploitable.");

    /// <summary>
    /// The two « Etape » columns are what the whole act reads; without them the file is some other
    /// document. Named separately from <see cref="SheetUnreadable"/> because the workbook opened
    /// fine — the user picked a real spreadsheet, just not this one.
    /// </summary>
    public static readonly Error LevelColumnsMissing = Error.Validation(
        "ReinscriptionSheet.LevelColumnsMissing",
        "Le fichier ne comporte pas les deux colonnes « Etape » (niveau actuel, puis niveau de "
        + "l'année de destination) : impossible de savoir où chaque étudiant est réinscrit.");

    public static readonly Error SameYear = Error.Validation(
        "ReinscriptionSheet.SameYear",
        "L'année de destination doit être différente de l'année clôturée.");

    public static readonly Error TargetYearNotLater = Error.Validation(
        "ReinscriptionSheet.TargetYearNotLater",
        "L'année de destination commence avant l'année clôturée — une réinscription va de l'avant.");

    /// <summary>
    /// The number of graduations the operator was shown does not match the number the plan now
    /// finds.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A checkbox would not do, and this is the one write of the act that needs the number.</b>
    /// Every other row lands on a student the file names; a « Diplômé » lands on a student it does
    /// <em>not</em> — so a registration created between the preview and the apply is a cursus ended
    /// by a confirmation nobody gave for it. Same guard, same reason, as
    /// <c>ApplyDeliberationCommand.ConfirmedDefaultCount</c>.
    /// </remarks>
    public static Error GraduationsNotConfirmed(int confirmed, int actual) => Error.Conflict(
        "ReinscriptionSheet.GraduationsNotConfirmed",
        $"La simulation annonçait {confirmed} diplômé(s) déduit(s) de leur absence du fichier, et le "
        + $"plan en trouve maintenant {actual}. Une inscription a changé entre-temps : relancez la "
        + "simulation et vérifiez le nombre avant d'appliquer.");

    /// <summary>
    /// One refusal for the whole file, naming how many lines are wrong and the first of them.
    /// </summary>
    /// <remarks>
    /// ⚠ The count and the first offending line both travel, because « 3 lignes en erreur » alone
    /// sends the user back to a 6 862-row spreadsheet with nowhere to start. The preview carries
    /// every one of them; this is the message on the apply, which is where somebody who skipped the
    /// preview finds out.
    /// </remarks>
    public static Error RowsRefused(int count, int firstSheetRow, string firstMessage) =>
        Error.Validation(
            "ReinscriptionSheet.RowsRefused",
            $"{count} ligne(s) du fichier ne peuvent pas être appliquées, à commencer par la ligne "
            + $"{firstSheetRow} : {firstMessage} Corrigez le fichier et relancez — la réinscription "
            + "est appliquée en totalité ou pas du tout.");
}
