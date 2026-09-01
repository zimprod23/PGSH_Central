using PGSH.SharedKernel;

namespace PGSH.Application.Exports;

public static class ExportErrors
{
    public static readonly Error NotAllowed = Error.Forbidden(
        "Export.NotAllowed",
        "Seule la scolarité peut exporter les listes d'étudiants et de stages.");

    /// <summary>
    /// ⚠ An export is the one read deliberately exempt from pagination, so it is the one read that
    /// can pull the whole base into memory. The refusal names the count and the axis that narrows
    /// it — « trop de lignes » on its own sends the user back to the same button.
    /// </summary>
    public static Error TooManyRows(int rowCount, int maximum, string narrowBy) => Error.Validation(
        "Export.TooManyRows",
        $"L'export porterait sur {ExportLabels.Count(rowCount)} lignes, au-delà de la limite de {ExportLabels.Count(maximum)}. "
        + $"Restreignez la sélection ({narrowBy}) et relancez.");
}
