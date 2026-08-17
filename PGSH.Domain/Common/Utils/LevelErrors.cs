using PGSH.SharedKernel;

namespace PGSH.Domain.Common.Utils;

public static class LevelErrors
{
    public static Error NotFound(int levelId) => Error.NotFound(
        "Levels.NotFound",
        $"The level with Id = '{levelId}' was not found.");

    /// <summary>
    /// The level is a marker, not a year of study — see <see cref="Level.IsPromotion"/>. Refused for
    /// every act that divides or fills a promotion: there is nobody to rotate, no stage to rotate
    /// through, and a partition on it would describe a division of the withdrawn.
    /// </summary>
    public static Error NotAPromotion(string levelLabel) => Error.Validation(
        "Levels.NotAPromotion",
        $"« {levelLabel} » n'est pas une promotion : c'est un marqueur de retrait hérité de l'ancienne "
        + "base, conservé pour que les inscriptions et les stages déjà effectués cette année-là ne "
        + "soient pas perdus. Il n'a ni stage ni cohorte, donc rien à répartir.");
}
