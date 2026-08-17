using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Occupancy;

/// <summary>
/// Which stages may send students here — the reverse of a stage's allowed-services list, which until
/// now could only be read from the stage's side. A service is where the pressure is felt, so "who is
/// allowed to put students on me" belongs on the service too.
/// </summary>
public sealed record GetServiceStagesQuery(int ServiceId) : IQuery<IReadOnlyList<ServiceStageResponse>>;

public sealed record ServiceStageResponse(
    int StageId,
    string StageName,
    int LevelId,
    string LevelLabel,
    int Capacity,
    /// <summary>
    /// ⚠ The stage lists this service, but the service's own quotas do not admit the stage's
    /// promotion — so <c>RotationArranger</c> drops it before building the rotation and publish
    /// would refuse it outright. A contradiction between two authored lists, and invisible from
    /// either side on its own.
    /// </summary>
    bool NotAdmitted);

internal sealed class GetServiceStagesQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetServiceStagesQuery, IReadOnlyList<ServiceStageResponse>>
{
    public async Task<Result<IReadOnlyList<ServiceStageResponse>>> Handle(
        GetServiceStagesQuery request, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services
            .AsNoTracking()
            .Include(s => s.LevelCapacities)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (service is null)
            return Result.Failure<IReadOnlyList<ServiceStageResponse>>(
                ServiceErrors.NotFound(request.ServiceId));

        // Small by construction — a stage lists at most 14 services in the current base, and the
        // reverse is smaller still. No pagination, deliberately: this is a configuration list, not a
        // data list, and a service nobody may use has to be able to show zero rows.
        var stages = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.AllowedServices.Any(a => a.Id == request.ServiceId))
            .OrderBy(s => s.Level.AcademicProgram)
            .ThenBy(s => s.Level.Year)
            .ThenBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.LevelId,
                LevelLabel = s.Level.Label ?? ("niveau " + s.LevelId),
            })
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ServiceStageResponse>>(
            stages.Select(s => new ServiceStageResponse(
                s.Id,
                s.Name,
                s.LevelId,
                s.LevelLabel,
                service.CapacityFor(s.LevelId),
                !service.Admits(s.LevelId)))
            .ToList());
    }
}
