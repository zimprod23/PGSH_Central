using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
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

        // Same reach as the create: a label identifies a roster within its promotion, and « Groupe 1 »
        // legitimately exists in several promotions of one année.
        bool labelConflict = await dbContext.AcademicGroups
            .AnyAsync(g => g.AcademicYearId == group.AcademicYearId
                        && g.LevelId == group.LevelId
                        && g.Label == request.Label
                        && g.Id != request.Id,
                      cancellationToken);

        if (labelConflict)
        {
            string promotion = group.LevelId is null
                ? "« Non réparti »"
                : await dbContext.Levels
                      .Where(l => l.Id == group.LevelId)
                      .Select(l => l.Label)
                      .FirstOrDefaultAsync(cancellationToken) ?? $"niveau {group.LevelId}";

            return Result.Failure(AcademicGroupErrors.DuplicateLabelInPromotion(request.Label, promotion));
        }

        // A partition is a division *of a promotion*, so the year's promotion-less bucket cannot carry
        // one: labelling it would hand every unassigned registration of every promotion to
        // CohortProvisioner as a single body.
        if (group.LevelId is null && !string.IsNullOrWhiteSpace(request.RotationGroup))
            return Result.Failure(AcademicGroupErrors.UnassignedRosterCannotBePartitioned(group.Label));

        group.Label          = request.Label;
        group.GeographicZone = request.GeographicZone;
        group.RotationGroup  = request.RotationGroup;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
