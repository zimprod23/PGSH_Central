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

    public static readonly Error NoPartitions = Error.Validation(
        "RotationCycle.NoPartitions",
        "Aucune partition n'est définie pour cette promotion — répartissez d'abord les groupes.");

    public static Error WrongWindowCount(int expected, int actual) => Error.Validation(
        "RotationCycle.WrongWindowCount",
        $"{expected} fenêtre(s) attendue(s) — {actual} fournie(s). Un bloc de S stages à k périodes "
        + "chacun occupe S × k fenêtres, le temps que chaque partition passe par chaque stage.");

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
