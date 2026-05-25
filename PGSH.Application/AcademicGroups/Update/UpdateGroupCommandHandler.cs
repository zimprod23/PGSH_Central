using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Update;

internal sealed class UpdateGroupCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<UpdateGroupCommand>
{
    public async Task<Result> Handle(UpdateGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await dbContext.AcademicGroups
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);

        if (group is null)
            return Result.Failure(Error.NotFound(
                "AcademicGroups.NotFound",
                $"The group with Id = '{request.Id}' was not found."));

        bool labelConflict = await dbContext.AcademicGroups
            .AnyAsync(g => g.AcademicYearId == group.AcademicYearId
                        && g.Label == request.Label
                        && g.Id != request.Id,
                      cancellationToken);

        if (labelConflict)
            return Result.Failure(Error.Conflict(
                "AcademicGroups.DuplicateLabel",
                $"A group with the label '{request.Label}' already exists for this year."));

        group.Label          = request.Label;
        group.GeographicZone = request.GeographicZone;
        group.RotationGroup  = request.RotationGroup;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
