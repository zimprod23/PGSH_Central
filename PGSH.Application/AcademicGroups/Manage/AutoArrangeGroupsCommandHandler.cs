using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage;

internal sealed class AutoArrangeGroupsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AutoArrangeGroupsCommand, BulkResponse<Guid, int>>
{
    public async Task<Result<BulkResponse<Guid, int>>> Handle(
        AutoArrangeGroupsCommand request, CancellationToken cancellationToken)
    {
        var registrations = await dbContext.Registrations
            .Where(r => r.LevelId == request.LevelId &&
                        r.AcademicYearId == request.AcademicYearId &&
                        r.AcademicGroupId == null)
            .OrderBy(r => r.Student.LastName)
            .ToListAsync(cancellationToken);

        if (!registrations.Any())
            return Result.Failure<BulkResponse<Guid, int>>(Error.NotFound(
                "Groups.NoUnassignedStudents",
                "No unassigned students found for the selected level and year."));

        // GroupNumber is unique per year — continue from the highest existing number
        // so running auto-arrange for multiple levels in the same year doesn't conflict
        int nextNumber = (await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Select(g => (int?)g.GroupNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        // Include the level label so admins can tell which level owns each group
        var levelLabel = await dbContext.Levels
            .Where(l => l.Id == request.LevelId)
            .Select(l => l.Label)
            .FirstOrDefaultAsync(cancellationToken)
            ?? $"Niveau {request.LevelId}";

        int groupCount = (int)Math.Ceiling((double)registrations.Count / request.GroupSize);

        var newGroups = Enumerable.Range(0, groupCount)
            .Select(i => new AcademicGroup
            {
                Label          = $"Groupe {nextNumber + i} — {levelLabel}",
                GroupNumber    = nextNumber + i,
                AcademicYearId = request.AcademicYearId,
            })
            .ToList();

        dbContext.AcademicGroups.AddRange(newGroups);
        await dbContext.SaveChangesAsync(cancellationToken);

        var itemResults = new List<BulkItemResult<Guid, int>>();
        for (int i = 0; i < newGroups.Count; i++)
        {
            foreach (var reg in registrations.Skip(i * request.GroupSize).Take(request.GroupSize))
            {
                reg.AcademicGroupId = newGroups[i].Id;
                itemResults.Add(new BulkItemResult<Guid, int>(reg.StudentId, newGroups[i].Id, null));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResponse<Guid, int>(
            itemResults,
            registrations.Count,
            itemResults.Count(x => x.IsSuccess),
            itemResults.Count(x => !x.IsSuccess)));
    }
}
