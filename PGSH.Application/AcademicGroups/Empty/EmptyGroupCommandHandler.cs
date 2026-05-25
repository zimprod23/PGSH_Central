using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Empty;

internal sealed class EmptyGroupCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<EmptyGroupCommand, int>
{
    public async Task<Result<int>> Handle(EmptyGroupCommand request, CancellationToken cancellationToken)
    {
        bool groupExists = await dbContext.AcademicGroups
            .AnyAsync(g => g.Id == request.GroupId, cancellationToken);

        if (!groupExists)
            return Result.Failure<int>(Error.NotFound(
                "AcademicGroups.NotFound",
                $"The group with Id = '{request.GroupId}' was not found."));

        var registrations = await dbContext.Registrations
            .Where(r => r.AcademicGroupId == request.GroupId)
            .ToListAsync(cancellationToken);

        foreach (var registration in registrations)
            registration.AcademicGroupId = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(registrations.Count);
    }
}
