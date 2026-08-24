using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

public static class AcademicYearErrors
{
    public static Error NotFound(int academicYearId) => Error.NotFound(
        "AcademicYears.NotFound",
        $"Aucune année universitaire ne porte l'identifiant '{academicYearId}'.");

    public static readonly Error NotAllowed = Error.Forbidden(
        "AcademicYears.NotAllowed",
        "Seule la scolarité peut créer, modifier ou supprimer une année universitaire.");

    public static readonly Error LabelRequired = Error.Validation(
        "AcademicYears.LabelRequired",
        "Une année universitaire doit porter un libellé.");

    public static Error DuplicateLabel(string label) => Error.Conflict(
        "AcademicYears.DuplicateLabel",
        $"Une année universitaire « {label} » existe déjà. Le libellé est ce que tout l'écran affiche : "
        + "deux années homonymes ne sont plus distinguables nulle part.");

    public static Error EndsBeforeItStarts(DateOnly startDate, DateOnly endDate) => Error.Validation(
        "AcademicYears.EndsBeforeItStarts",
        $"L'année se terminerait le {endDate:dd/MM/yyyy}, avant son début le {startDate:dd/MM/yyyy}.");

    /// <summary>
    /// ⚠ Not a tidiness rule. <c>ServiceOccupancyCalculator</c> bounds a year by its <em>dates</em>
    /// rather than by its id — deliberately, since the two cannot disagree — so a day belonging to two
    /// years makes every slot in the overlap count twice against a service's load.
    /// </summary>
    public static Error OverlapsAnotherYear(string label, string otherLabel) => Error.Conflict(
        "AcademicYears.OverlapsAnotherYear",
        $"« {label} » chevaucherait « {otherLabel} ». Deux années universitaires ne peuvent pas "
        + "partager une journée : l'effectif d'un service est mesuré sur les dates, et une journée "
        + "comptée deux fois fausse la charge de chaque service concerné.");

    public static Error AlreadyCurrent(string label) => Error.Conflict(
        "AcademicYears.AlreadyCurrent",
        $"« {label} » est déjà l'année en cours.");

    /// <summary>
    /// Deleting the year every unscoped handler resolves to leaves the application with no answer to
    /// « quelle année ? ». Designate another one first — that act is reversible, this one is not.
    /// </summary>
    public static Error CannotDeleteCurrent(string label) => Error.Conflict(
        "AcademicYears.CannotDeleteCurrent",
        $"« {label} » est l'année en cours : la supprimer laisserait l'application sans année de "
        + "référence. Désignez d'abord une autre année en cours.");

    /// <summary>
    /// The year constitutes rows that would be orphaned or destroyed. Each count is named because
    /// « impossible de supprimer » without saying what stands in the way sends the user hunting.
    /// </summary>
    public static Error StillInUse(string label, IReadOnlyList<string> holdings) => Error.Conflict(
        "AcademicYears.StillInUse",
        $"« {label} » ne peut pas être supprimée : {string.Join(", ", holdings)}. Une année universitaire "
        + "constitue ces lignes — les supprimer avec elle effacerait le dossier de cette année.");
}
