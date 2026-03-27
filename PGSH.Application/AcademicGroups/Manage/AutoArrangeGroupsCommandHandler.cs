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
        // 1. Fetch unassigned registrations
        var registrations = await dbContext.Registrations
            .Where(r => r.LevelId == request.LevelId &&
                        r.AcademicYearId == request.AcademicYearId &&
                        r.AcademicGroupId == null)
            .OrderBy(r => r.Student.LastName) // Optional: Sort alphabetically
            .ToListAsync(cancellationToken);

        if (!registrations.Any())
            return Result.Failure<BulkResponse<Guid, int>>(Error.Problem("Groups.Empty", "No students to arrange."));

        var itemResults = new List<BulkItemResult<Guid, int>>();
        int totalStudents = registrations.Count;
        int groupCount = (int)Math.Ceiling((double)totalStudents / request.GroupSize);

        // 2. Create the AcademicGroup entities
        var newGroups = new List<AcademicGroup>();
        for (int i = 1; i <= groupCount; i++)
        {
            newGroups.Add(new AcademicGroup
            {
                Label = $"Group {i}",
                GroupNumber = i,
                AcademicYearId = request.AcademicYearId
            });
        }

        dbContext.AcademicGroups.AddRange(newGroups);
        await dbContext.SaveChangesAsync(cancellationToken); // Save to get the new Group IDs

        // 3. Distribute Students into the newly created Groups
        int currentStudentIndex = 0;
        foreach (var group in newGroups)
        {
            var studentsForThisGroup = registrations
                .Skip(currentStudentIndex)
                .Take(request.GroupSize)
                .ToList();

            foreach (var reg in studentsForThisGroup)
            {
                reg.AcademicGroupId = group.Id;
                itemResults.Add(new BulkItemResult<Guid, int>(reg.StudentId, group.Id, null));
            }

            currentStudentIndex += request.GroupSize;
        }

        // 4. Final Save for Registrations
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResponse<Guid, int>(
            itemResults,
            totalStudents,
            itemResults.Count(x => x.IsSuccess),
            itemResults.Count(x => !x.IsSuccess)
        ));
    }
}
