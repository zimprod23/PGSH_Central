using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
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

        // Preserve existing labels; only fill in groups that have none
        var existingLabels = groups
            .Select(g => g.RotationGroup)
            .OfType<string>()
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        int numPartitions = existingLabels.Count > 0
            ? existingLabels.Count
            : Math.Max(1, request.PartitionCount);

        var labels = Enumerable.Range(0, numPartitions)
            .Select(i => ((char)('A' + (i % 26))).ToString() + (i >= 26 ? (i / 26).ToString() : ""))
            .ToList();

        foreach (var l in existingLabels.Where(l => !labels.Contains(l)))
            labels.Add(l);

        var partitionCounts = labels.ToDictionary(l => l, l => groups.Count(g => g.RotationGroup == l));
        var unassigned      = groups.Where(g => g.RotationGroup is null).ToList();

        foreach (var group in unassigned)
        {
            var label = partitionCounts.MinBy(kvp => kvp.Value).Key;
            group.RotationGroup = label;
            partitionCounts[label]++;
        }

        if (unassigned.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(unassigned.Count);
    }
}
