using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Delete;

internal sealed class DeleteGroupCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<DeleteGroupCommand>
{
    public async Task<Result> Handle(DeleteGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await dbContext.AcademicGroups
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);

        if (group is null)
            return Result.Failure(Error.NotFound(
                "AcademicGroups.NotFound",
                $"The group with Id = '{request.Id}' was not found."));

        bool hasCohorts = await dbContext.Cohorts
            .AnyAsync(c => c.AcademicGroupId == request.Id, cancellationToken);

        if (hasCohorts)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.HasCohorts",
                "Cannot delete a group that has active cohorts. Remove all cohorts first."));

        bool hasStudents = await dbContext.Registrations
            .AnyAsync(r => r.AcademicGroupId == request.Id, cancellationToken);

        if (hasStudents)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.HasStudents",
                "Cannot delete a group that has students assigned. Empty the group first."));

        dbContext.AcademicGroups.Remove(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
