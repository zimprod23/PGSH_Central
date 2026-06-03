using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Empty;

internal sealed class EmptyAllYearGroupsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<EmptyAllYearGroupsCommand, int>
{
    public async Task<Result<int>> Handle(EmptyAllYearGroupsCommand request, CancellationToken cancellationToken)
    {
        var groupIds = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        if (groupIds.Count == 0)
            return Result.Success(0);

        int unassigned = await dbContext.Registrations
            .Where(r => r.AcademicGroupId != null && groupIds.Contains(r.AcademicGroupId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AcademicGroupId, (int?)null), cancellationToken);

        return Result.Success(unassigned);
    }
}
