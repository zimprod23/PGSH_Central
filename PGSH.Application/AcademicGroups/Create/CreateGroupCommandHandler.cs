using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Create;

internal sealed class CreateGroupCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateGroupCommand, int>
{
    public async Task<Result<int>> Handle(CreateGroupCommand request, CancellationToken cancellationToken)
    {
        bool yearExists = await dbContext.AcademicYears
            .AnyAsync(y => y.Id == request.AcademicYearId, cancellationToken);

        if (!yearExists)
            return Result.Failure<int>(Error.NotFound(
                "AcademicYears.NotFound",
                $"Academic year '{request.AcademicYearId}' not found."));

        bool labelConflict = await dbContext.AcademicGroups
            .AnyAsync(g => g.AcademicYearId == request.AcademicYearId
                        && g.Label == request.Label,
                      cancellationToken);

        if (labelConflict)
            return Result.Failure<int>(Error.Conflict(
                "AcademicGroups.DuplicateLabel",
                $"A group with the label '{request.Label}' already exists for this year."));

        int nextNumber = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId)
            .Select(g => (int?)g.GroupNumber)
            .MaxAsync(cancellationToken) ?? 0;

        if (request.LevelId.HasValue)
        {
            bool levelExists = await dbContext.Levels
                .AnyAsync(l => l.Id == request.LevelId.Value, cancellationToken);

            if (!levelExists)
                return Result.Failure<int>(Error.NotFound(
                    "Levels.NotFound",
                    $"Level '{request.LevelId}' not found."));
        }

        var group = new AcademicGroup
        {
            Label          = request.Label,
            AcademicYearId = request.AcademicYearId,
            LevelId        = request.LevelId,
            GroupNumber    = nextNumber + 1,
            GeographicZone = request.GeographicZone,
            RotationGroup  = request.RotationGroup,
        };

        dbContext.AcademicGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return group.Id;
    }
}
