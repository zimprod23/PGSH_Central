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

        string? levelLabel = null;

        if (request.LevelId.HasValue)
        {
            var level = await dbContext.Levels
                .Where(l => l.Id == request.LevelId.Value)
                .Select(l => new { l.Id, l.Label })
                .FirstOrDefaultAsync(cancellationToken);

            if (level is null)
                return Result.Failure<int>(Error.NotFound(
                    "Levels.NotFound",
                    $"Level '{request.LevelId}' not found."));

            levelLabel = level.Label ?? $"niveau {level.Id}";
        }
        else if (!string.IsNullOrWhiteSpace(request.RotationGroup))
        {
            // A roster with no promotion is « Non réparti » — see AcademicGroupErrors. Refusing the
            // label at the point it would be written is what keeps CohortProvisioner's LevelId-only
            // reach sufficient.
            return Result.Failure<int>(
                AcademicGroupErrors.UnassignedRosterCannotBePartitioned(request.Label));
        }

        // ⚠ Scoped to the promotion, not to the year. « Groupe 1 » exists in the 3rd year and in the
        // 5th year of the same année — that is how the faculty names them, and a label is read
        // alongside the promotion it belongs to. Held to the year alone, creating the 4th year's
        // « Groupe 1 » failed because the 3rd year already had one.
        bool labelConflict = await dbContext.AcademicGroups
            .AnyAsync(g => g.AcademicYearId == request.AcademicYearId
                        && g.LevelId == request.LevelId
                        && g.Label == request.Label,
                      cancellationToken);

        if (labelConflict)
            return Result.Failure<int>(AcademicGroupErrors.DuplicateLabelInPromotion(
                request.Label, levelLabel ?? "« Non réparti »"));

        // Numbered within its promotion, not within the year: a number only means anything alongside
        // the promotion it counts in, which is what IX_AcademicGroup_Year_Level_Number now says.
        int nextNumber = await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId && g.LevelId == request.LevelId)
            .Select(g => (int?)g.GroupNumber)
            .MaxAsync(cancellationToken) ?? 0;

        // ⚠ The one place a human can ask for either shape, so it is the one place the two factories
        // have to be chosen between rather than merged: a level-less request is « Non réparti », and
        // saying so here is what keeps « I forgot the promotion » from producing the bucket in
        // silence. The refusal above has already made a partitioned bucket impossible.
        var made = request.LevelId is { } levelId
            ? AcademicGroup.ForPromotion(
                request.AcademicYearId, levelId, nextNumber + 1, request.Label,
                request.GeographicZone, request.RotationGroup, request.Purpose)
            : AcademicGroup.AsUnassignedBucket(
                request.AcademicYearId, request.Label, request.GeographicZone, request.Purpose);

        if (made.IsFailure)
            return Result.Failure<int>(made.Error);

        var group = made.Value;

        dbContext.AcademicGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        return group.Id;
    }
}
