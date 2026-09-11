using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.PromotionFit;

public static class PromotionFitErrors
{
    /// <summary>
    /// The year holds no promotion to plan. ⚠ Not the same thing as a promotion that does not fit —
    /// that is a row this read describes. This is « rien à lire », and a panel of nothing would read
    /// as the faculty having no students.
    /// </summary>
    public static Error NoPromotionsInYear(string yearLabel) => Error.NotFound(
        "PromotionFit.NoPromotionsInYear",
        $"Aucune promotion n'a d'inscription sur « {yearLabel} » : il n'y a rien à planifier pour "
        + "cette année. Ouvrez les inscriptions, ou choisissez une autre année.");
}
