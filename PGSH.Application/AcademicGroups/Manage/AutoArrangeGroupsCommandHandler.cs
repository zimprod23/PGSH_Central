using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage;

internal class AutoArrangeGroupsCommandHandler(IApplicationDbContext dbContext) : ICommandHandler<AutoArrangeGroupsCommand, BulkResponse<Guid, int>>
{
    public async Task<Result<BulkResponse<Guid, int>>> Handle(AutoArrangeGroupsCommand request, CancellationToken cancellationToken)
    {
        var registrations = await dbContext.Registrations
            .Where(r => r.LevelId == request.LevelId &&
                        r.AcademicYearId == request.AcademicYearId &&
                        r.AcademicGroupId == null)
            .OrderBy(r => r.Student.LastName)
            .ToListAsync(cancellationToken);

        if (!registrations.Any())
            return Result.Failure<BulkResponse<Guid, int>>(Error.Problem("Groups.Empty", "No unassigned students found for the selected level and year."));

        int totalStudents = registrations.Count;
        int groupCount = (int)Math.Ceiling((double)totalStudents / request.GroupSize);

        var newGroups = Enumerable.Range(1, groupCount)
            .Select(i => new AcademicGroup { Label = $"Groupe {i}", GroupNumber = i, AcademicYearId = request.AcademicYearId })
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
            totalStudents,
            itemResults.Count(x => x.IsSuccess),
            itemResults.Count(x => !x.IsSuccess)));
    }
}
