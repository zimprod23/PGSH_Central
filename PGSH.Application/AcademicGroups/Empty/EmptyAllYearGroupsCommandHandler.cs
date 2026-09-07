using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Empty;

/// <summary>
/// Empties the rosters of a year, or of one promotion inside it — and only ever the roster pointers.
/// </summary>
/// <remarks>
/// <para>Refused while the rosters in scope hold any affectation at all, whether or not it has
/// started: the pointer is not what a rotation hangs off, so clearing it in bulk would leave the
/// planning attached to rosters displaying 0 étudiants. See <see cref="EmptyGroupCommandHandler"/>
/// for why the affectations cannot simply be taken along.</para>
///
/// <para>⚠ <b>The scope of the refusal is the scope of the act.</b> Read year-wide, the toll counts
/// every promotion — so a promotion whose planning was cleared could not be re-découpée until every
/// <i>other</i> promotion of the year had been cleared too, which is not a rule anybody meant.</para>
/// </remarks>
internal sealed class EmptyAllYearGroupsCommandHandler(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader)
    : ICommandHandler<EmptyAllYearGroupsCommand, int>
{
    public async Task<Result<int>> Handle(
        EmptyAllYearGroupsCommand request, CancellationToken cancellationToken)
    {
        int? levelId = request.LevelId;

        string? levelLabel = null;

        if (levelId is not null)
        {
            levelLabel = await dbContext.Levels
                .Where(l => l.Id == levelId)
                .Select(l => l.Label)
                .FirstOrDefaultAsync(cancellationToken);

            if (levelLabel is null)
                return Result.Failure<int>(LevelErrors.NotFound(levelId.Value));
        }

        var groupIds = await RosterScope.Query(dbContext, request.AcademicYearId, levelId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        if (groupIds.Count == 0)
            return Result.Success(0);

        var toll = levelId is null
            ? await tollReader.ForYearRostersAsync(request.AcademicYearId, cancellationToken)
            : await tollReader.ForPromotionRostersAsync(
                request.AcademicYearId, levelId.Value, cancellationToken);

        if (!toll.IsEmpty)
        {
            string yearLabel = await dbContext.AcademicYears
                .Where(y => y.Id == request.AcademicYearId)
                .Select(y => y.Label)
                .FirstOrDefaultAsync(cancellationToken) ?? $"l'année {request.AcademicYearId}";

            return Result.Failure<int>(levelId is null
                ? AcademicGroupErrors.YearRostersHaveAffectations(
                    yearLabel, toll.Assignments, toll.Periods)
                : AcademicGroupErrors.PromotionRostersHaveAffectations(
                    levelLabel!, yearLabel, toll.Assignments, toll.Periods));
        }

        int unassigned = await dbContext.Registrations
            .Where(r => r.AcademicGroupId != null && groupIds.Contains(r.AcademicGroupId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AcademicGroupId, (int?)null), cancellationToken);

        return Result.Success(unassigned);
    }
}
