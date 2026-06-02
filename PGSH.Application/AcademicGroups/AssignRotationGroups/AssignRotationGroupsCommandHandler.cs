using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

internal sealed class AssignRotationGroupsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AssignRotationGroupsCommand, int>
{
    public async Task<Result<int>> Handle(AssignRotationGroupsCommand request, CancellationToken cancellationToken)
    {
        if (request.PartitionCount < 1)
            return Result.Failure<int>(Error.Validation("Partitions.InvalidCount", "Partition count must be at least 1."));

        var groups = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .OrderBy(g => g.GroupNumber)
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
            return Result.Success(0);

        var assignments = PartitionAllocator.AssignUnlabelled(
            groups.Select(g => (g.Id, g.RotationGroup)).ToList(),
            request.PartitionCount);

        foreach (var group in groups.Where(g => assignments.ContainsKey(g.Id)))
            group.RotationGroup = assignments[group.Id];

        if (assignments.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(assignments.Count);
    }
}
