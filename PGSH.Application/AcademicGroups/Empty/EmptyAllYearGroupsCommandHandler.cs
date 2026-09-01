using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Empty;

/// <summary>
/// Empties every roster of a year — and only ever the roster pointers.
/// </summary>
/// <remarks>
/// Refused while the year's rosters hold any affectation at all, whether or not it has started: the
/// pointer is not what a rotation hangs off, so clearing it year-wide would leave the entire planning
/// of the year attached to rosters displaying 0 étudiants. See
/// <see cref="EmptyGroupCommandHandler"/> for why the affectations cannot simply be taken along.
/// </remarks>
internal sealed class EmptyAllYearGroupsCommandHandler(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader)
    : ICommandHandler<EmptyAllYearGroupsCommand, int>
{
    public async Task<Result<int>> Handle(
        EmptyAllYearGroupsCommand request, CancellationToken cancellationToken)
    {
        var groupIds = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        if (groupIds.Count == 0)
            return Result.Success(0);

        var toll = await tollReader.ForYearRostersAsync(request.AcademicYearId, cancellationToken);

        if (!toll.IsEmpty)
        {
            string yearLabel = await dbContext.AcademicYears
                .Where(y => y.Id == request.AcademicYearId)
                .Select(y => y.Label)
                .FirstOrDefaultAsync(cancellationToken) ?? $"l'année {request.AcademicYearId}";

            return Result.Failure<int>(AcademicGroupErrors.YearRostersHaveAffectations(
                yearLabel, toll.Assignments, toll.Periods));
        }

        int unassigned = await dbContext.Registrations
            .Where(r => r.AcademicGroupId != null && groupIds.Contains(r.AcademicGroupId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AcademicGroupId, (int?)null), cancellationToken);

        return Result.Success(unassigned);
    }
}
