using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Employees.MyServices;

/// <summary>
/// Single source of truth for "each chef controls only his own services". A service
/// period (and its evaluation) may be acted on by an administrative role or by the
/// chef of that period's service — nobody else. Shared by the execution write handlers
/// so the rule lives in one place.
/// </summary>
internal sealed class ExecutionAuthorizer(IApplicationDbContext dbContext, IUserContext userContext)
{
    private bool IsAdministrative =>
        Roles.Administrative.Any(userContext.IsInRole);

    // IUserContext.UserId is the Keycloak subject; the local Employee/User PK that
    // Service.ServiceChefId references is a different Guid linked by IdentityProviderId.
    private async Task<Guid?> CurrentEmployeeIdAsync(CancellationToken ct) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(u => u.IdentityProviderId == userContext.UserId.ToString())
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Service ids the current user is chef of (empty for administrative users with no chef role).</summary>
    public async Task<List<int>> ChefServiceIdsAsync(CancellationToken ct)
    {
        var employeeId = await CurrentEmployeeIdAsync(ct);
        if (employeeId is null)
            return [];

        return await dbContext.Services
            .AsNoTracking()
            .Where(s => s.ServiceChefId == employeeId)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }

    public async Task<Result> EnsureCanActOnPeriodAsync(Guid periodId, CancellationToken ct)
    {
        var chefId = await dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId)
            .Select(p => (Guid?)p.Service.ServiceChefId)
            .FirstOrDefaultAsync(ct);

        // No row → the period does not exist; let the caller surface NotFound.
        if (chefId is null && !await dbContext.ServicePeriods.AnyAsync(p => p.Id == periodId, ct))
            return Result.Failure(StageErrors.PeriodNotFound(periodId));

        if (IsAdministrative)
            return Result.Success();

        var employeeId = await CurrentEmployeeIdAsync(ct);
        return employeeId is not null && chefId == employeeId
            ? Result.Success()
            : Result.Failure(StageErrors.NotServiceChef);
    }

    public async Task<Result> EnsureCanActOnEvaluationAsync(Guid evaluationId, CancellationToken ct)
    {
        var periodId = await dbContext.ServiceEvaluation
            .AsNoTracking()
            .Where(e => e.Id == evaluationId)
            .Select(e => (Guid?)e.ServicePeriodId)
            .FirstOrDefaultAsync(ct);

        if (periodId is null)
            return Result.Failure(StageErrors.EvaluationNotFound(evaluationId));

        return await EnsureCanActOnPeriodAsync(periodId.Value, ct);
    }
}
