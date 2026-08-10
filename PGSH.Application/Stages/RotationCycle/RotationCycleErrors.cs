using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

public static class RotationCycleErrors
{
    public static readonly Error NoStages = Error.Validation(
        "RotationCycle.NoStages",
        "Indiquez au moins un stage à faire tourner.");

    public static readonly Error DuplicateStage = Error.Validation(
        "RotationCycle.DuplicateStage",
        "Un même stage ne peut pas figurer deux fois dans un bloc.");

    public static readonly Error InvalidPeriods = Error.Validation(
        "RotationCycle.InvalidPeriods",
        "Chaque stage occupe au moins une période.");

    public static readonly Error NoPartitions = Error.Validation(
        "RotationCycle.NoPartitions",
        "Aucune partition n'est définie pour cette promotion — répartissez d'abord les groupes.");

    public static Error WrongWindowCount(
        int expected, int actual, IReadOnlyList<RotationStage> stages) => Error.Validation(
        "RotationCycle.WrongWindowCount",
        $"{expected} fenêtre(s) attendue(s) — {actual} fournie(s). Le bloc occupe "
        + string.Join(" + ", stages.Select(s => s.Periods))
        + $" = {expected} colonnes : le temps qu'une partition passe par chaque stage.");

    /// <summary>
    /// Concurrency is <c>P·kₛ/T</c> and has to come out whole, which pins <c>P</c> to a multiple of
    /// <c>T / gcd(kₛ)</c>. Naming that multiple is the whole value of the message.
    /// </summary>
    public static Error PartitionCountIncompatible(int partitionCount, int step, int timeline) => Error.Validation(
        "RotationCycle.PartitionCountIncompatible",
        $"{partitionCount} partitions ne se répartissent pas sur un bloc de {timeline} colonnes. Le "
        + $"nombre de partitions doit être un multiple de {step} — {step}, {step * 2}, {step * 3}… — "
        + "sinon le nombre de partitions présentes dans un stage à un instant donné n'est pas entier.");

    /// <summary>
    /// The counting conditions hold but no arrangement exists. Rare, and genuinely a mathematical dead end
    /// rather than a bug — the search is exhaustive.
    /// </summary>
    public static Error NoFeasibleArrangement(int partitionCount, int timeline) => Error.Validation(
        "RotationCycle.NoFeasibleArrangement",
        $"Aucune répartition ne satisfait ces durées avec {partitionCount} partitions sur {timeline} "
        + "colonnes, bien que les comptes soient cohérents. La recherche est exhaustive : il n'en existe "
        + "pas. Essayez un multiple supérieur de partitions, ou scindez le bloc en semestres.");

    /// <summary>
    /// The search hit its budget before deciding. Kept distinct from <see cref="NoFeasibleArrangement"/>
    /// because that one asserts a proof, and asserting one we did not finish would be a lie.
    /// </summary>
    public static Error ArrangementUndetermined(int partitionCount, int timeline) => Error.Validation(
        "RotationCycle.ArrangementUndetermined",
        $"La recherche d'une répartition pour {partitionCount} partitions sur {timeline} colonnes n'a pas "
        + "abouti dans les limites allouées — sans conclure qu'elle est impossible. Réduisez le nombre de "
        + "partitions, ou scindez le bloc.");

    public static Error WindowsOverlap(int first, int second) => Error.Validation(
        "RotationCycle.WindowsOverlap",
        $"Les fenêtres {first} et {second} se chevauchent — les colonnes d'un même bloc se suivent.");

    public static Error StageNotOfLevel(int stageId, string levelLabel) => Error.Validation(
        "RotationCycle.StageNotOfLevel",
        $"Le stage {stageId} n'appartient pas à « {levelLabel} ».");

    public static Error CannotReplacePublished(int publishedCells) => Error.Conflict(
        "RotationCycle.CannotReplacePublished",
        $"{publishedCells} créneau(x) de ce bloc sont déjà publiés — des étudiants y ont été envoyés. "
        + "Redéfinir l'axe laisserait le planning publié décrire des fenêtres qui n'existent plus.");
}
