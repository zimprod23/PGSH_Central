using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.OccupancyReport;

public static class OccupancyReportErrors
{
    /// <summary>
    /// Not the same thing as « aucune cellule » — that is a planning state the report describes.
    /// This is a filter that selects no service at all, and a report of nothing would read as the
    /// faculty having no services.
    /// </summary>
    public static readonly Error NoServicesInScope = Error.NotFound(
        "OccupancyReport.NoServicesInScope",
        "Aucun service ne correspond à cette sélection.");

    /// <summary>
    /// The refusal names the count and the axis that narrows it — « trop de lignes » on its own
    /// sends the user back to the same button.
    /// </summary>
    public static Error TooManyPlacements(int count, int maximum) => Error.Validation(
        "OccupancyReport.TooManyPlacements",
        $"Le rapport porterait sur {count} cellules de répartition, au-delà de la limite de {maximum}. "
        + "Restreignez la sélection (un hôpital, une promotion ou un stage) et relancez.");
}
