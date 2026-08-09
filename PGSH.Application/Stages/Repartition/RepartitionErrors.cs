using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Repartition;

public static class RepartitionErrors
{
    public static Error LevelNotFound(int levelId) => Error.NotFound(
        "Levels.NotFound",
        $"Level '{levelId}' not found.");
}
