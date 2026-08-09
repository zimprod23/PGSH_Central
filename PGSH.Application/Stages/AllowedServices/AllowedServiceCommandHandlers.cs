using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.AllowedServices;

internal sealed class AddAllowedServiceCommandHandler(IApplicationDbContext dbContext)
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

        stage.AllowedServices.Add(service);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
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

internal sealed class RemoveAllowedServiceCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<RemoveAllowedServiceCommand>
{
    public async Task<Result> Handle(RemoveAllowedServiceCommand request, CancellationToken cancellationToken)
    {
        var stage = await dbContext.Stages
            .Include(s => s.AllowedServices)
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);

        if (stage is null)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        var service = stage.AllowedServices.FirstOrDefault(s => s.Id == request.ServiceId);
        if (service is null)
            return Result.Success();

        stage.AllowedServices.Remove(service);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
