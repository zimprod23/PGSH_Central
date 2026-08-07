using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Evaluations;

/// <summary>
/// Resolves the <see cref="StageObjective"/>s an evaluation grades, and refuses any that do not belong
/// to the stage being evaluated.
///
/// Both matter. The navigation has to be attached or <see cref="StageScoring"/> weighs every objective
/// 1 and the period mark comes out wrong; and an id from another stage would otherwise be accepted
/// silently (weighted 1 against a stage it has nothing to do with) while an id that exists nowhere
/// would surface as a foreign-key 500 instead of a validation error.
/// </summary>
internal sealed class EvaluationObjectiveResolver(IApplicationDbContext dbContext)
{
    public async Task<Result<IReadOnlyDictionary<int, StageObjective>>> ResolveAsync(
        int stageId, IEnumerable<int> objectiveIds, CancellationToken ct)
    {
        var requested = objectiveIds.Distinct().ToList();
        if (requested.Count == 0)
            return Result.Success<IReadOnlyDictionary<int, StageObjective>>(
                new Dictionary<int, StageObjective>());

        var objectives = await dbContext.StageObjectives
            .Where(o => o.StageId == stageId && requested.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct);

        var foreign = requested.Where(id => !objectives.ContainsKey(id)).ToList();
        return foreign.Count > 0
            ? Result.Failure<IReadOnlyDictionary<int, StageObjective>>(
                StageErrors.ObjectiveNotInStage(foreign[0]))
            : Result.Success<IReadOnlyDictionary<int, StageObjective>>(objectives);
    }
}
