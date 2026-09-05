using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.AllowedServices;

internal sealed class AddAllowedServiceCommandHandler(
    IApplicationDbContext dbContext,
    ServiceRankWriter rankWriter)
    : ICommandHandler<AddAllowedServiceCommand>
{
    public async Task<Result> Handle(AddAllowedServiceCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .Include(s => s.AllowedServices)
            .Include(s => s.Level)
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);

        if (stage is null)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        var service = await dbContext.Services
            .Include(s => s.LevelCapacities)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure(ServiceErrors.NotFound(request.ServiceId));

        if (stage.AllowedServices.Any(s => s.Id == request.ServiceId))
            return Result.Success();

        // A service whose quotas exclude this stage's promotion can never host it: auto-arrange
        // would drop it from the rotation and publish would reject any cell placed on it. Catching
        // it here means the list only ever contains services the stage can actually use.
        if (!service.Admits(stage.LevelId))
        {
            var admitted = await DescribeAdmittedLevelsAsync(service, cancellationToken);
            return Result.Failure(StageErrors.ServiceDoesNotAdmitStageLevel(
                service.Name, LabelOf(stage.Level, stage.LevelId), admitted));
        }

        // Appended, never inserted: the rank decides which run of group numbers the service
        // receives, and a service newly added to the list has no claim on a position somebody chose
        // for the ones already there. Reordering afterwards is its own act.
        //
        // ⚠ The write goes through the rank writer rather than through stage.AllowedServices, so the
        // insertion and the re-basing land in one transaction. Adding to the navigation here would
        // also be detached by the ChangeTracker.Clear() that opens each attempt.
        return await rankWriter.AppendAsync(request.StageId, request.ServiceId, cancellationToken);
    }

    /// <summary>The promotions the service does take, named — a refusal that lists them is one the
    /// user can act on without opening the service's fiche.</summary>
    private async Task<List<string>> DescribeAdmittedLevelsAsync(
        Service service, CancellationToken cancellationToken)
    {
        var levelIds = service.LevelCapacities.Select(c => c.LevelId).ToList();

        return await dbContext.Levels
            .AsNoTracking()
            .Where(l => levelIds.Contains(l.Id))
            .OrderBy(l => l.AcademicProgram)
            .ThenBy(l => l.Year)
            .Select(l => (l.Label ?? (l.Year + "e année")) + " " + l.AcademicProgram)
            .ToListAsync(cancellationToken);
    }

    private static string LabelOf(Level? level, int levelId) =>
        level is null ? $"niveau {levelId}" : $"{level.Label ?? $"{level.Year}e année"} {level.AcademicProgram}";
}

internal sealed class RemoveAllowedServiceCommandHandler(
    IApplicationDbContext dbContext,
    ServiceRankWriter rankWriter)
    : ICommandHandler<RemoveAllowedServiceCommand>
{
    public async Task<Result> Handle(RemoveAllowedServiceCommand request, CancellationToken cancellationToken)
    {
        bool stageExists = await dbContext.Stages
            .AnyAsync(s => s.Id == request.StageId, cancellationToken);

        if (!stageExists)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        // The join row is what is being removed, so it is what is asked about — the Include of the
        // whole service list only existed to answer this one question. Idempotent: a service the
        // stage does not authorise is already in the state the caller wants.
        bool isAllowed = await dbContext.StageAllowedServices.AnyAsync(
            a => a.StageId == request.StageId && a.ServiceId == request.ServiceId, cancellationToken);

        if (!isAllowed)
            return Result.Success();

        // The survivors keep their relative order with the hole closed, so the rank beside a service
        // is always the place it actually takes in the queue. Removal and re-basing are one act and
        // therefore one transaction — see ServiceRankWriter.
        return await rankWriter.RemoveAsync(request.StageId, request.ServiceId, cancellationToken);
    }
}
