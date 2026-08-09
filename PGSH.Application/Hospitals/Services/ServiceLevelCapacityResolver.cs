using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services;

/// <summary>
/// Turns the intake rules an admin form sends into a set the domain will accept: every level real,
/// no level named twice. Shared by create and update so the two cannot drift — a rule enforced on
/// one path only is not enforced.
///
/// Deliberately does <b>not</b> compare a quota against <c>Service.Capacity</c>: quotas replace that
/// number rather than sitting under it, so on a restricted service it governs nothing and a quota
/// above it contradicts nothing.
/// </summary>
internal sealed class ServiceLevelCapacityResolver(IApplicationDbContext dbContext)
{
    public async Task<Result<IReadOnlyCollection<(int LevelId, int Capacity)>>> ResolveAsync(
        IReadOnlyCollection<ServiceLevelCapacityRequest>? quotas,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<(int, int)> none = [];
        if (quotas is null || quotas.Count == 0)
            return Result.Success(none);

        var duplicate = quotas
            .GroupBy(q => q.LevelId)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            return Result.Failure<IReadOnlyCollection<(int, int)>>(
                ServiceErrors.DuplicateLevelQuota(duplicate.Key));

        var levelIds = quotas.Select(q => q.LevelId).ToList();
        var known = await dbContext.Levels
            .AsNoTracking()
            .Where(l => levelIds.Contains(l.Id))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        int unknown = levelIds.Except(known).FirstOrDefault();
        if (unknown != 0)
            return Result.Failure<IReadOnlyCollection<(int, int)>>(ServiceErrors.UnknownLevel(unknown));

        return Result.Success<IReadOnlyCollection<(int, int)>>(
            quotas.Select(q => (q.LevelId, q.Capacity)).ToList());
    }
}
