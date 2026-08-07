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

    /// <summary>The local User PK of the current caller (null if no linked profile) — for audit stamps.</summary>
    public Task<Guid?> CurrentUserIdAsync(CancellationToken ct) => CurrentEmployeeIdAsync(ct);

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

    /// <summary>
    /// Service ids the current user is responsible for as chef <em>or</em> staff member. This is the
    /// broader scope used for presence: a secretary attached to a service (via its staff) manages that
    /// service's attendance, exactly like its chef. Empty when the user leads/staffs no service.
    /// </summary>
    public async Task<List<int>> MyServiceIdsAsync(CancellationToken ct)
    {
        var employeeId = await CurrentEmployeeIdAsync(ct);
        if (employeeId is null)
            return [];

        return await dbContext.Services
            .AsNoTracking()
            .Where(s => s.ServiceChefId == employeeId || s.Staff.Any(e => e.Id == employeeId))
            .Select(s => s.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Read scoping for presence, which is deliberately wider than the write scope: everyone who may
    /// record it, plus the student whose own rotation it is. Consulting your own attendance is not a
    /// privileged act — gating it behind the recording scope made the student portal fire a 403 on
    /// every stage it displayed.
    /// </summary>
    public async Task<Result> EnsureCanReadAttendanceAsync(Guid periodId, CancellationToken ct)
    {
        var canRecord = await EnsureCanRecordAttendanceAsync(periodId, ct);
        if (canRecord.IsSuccess)
            return canRecord;

        // Not found outranks forbidden: keep the original error when the period does not exist.
        if (canRecord.Error == StageErrors.PeriodNotFound(periodId))
            return canRecord;

        var currentUserId = await CurrentEmployeeIdAsync(ct);
        if (currentUserId is null)
            return canRecord;

        bool ownsPeriod = await dbContext.ServicePeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId
                        && p.InternshipAssignment.Registration.StudentId == currentUserId.Value, ct);

        return ownsPeriod ? Result.Success() : canRecord;
    }

    /// <summary>
    /// Presence write scoping: a period's attendance may be recorded by a global administrative user,
    /// or by the chef or staff of that period's service — nobody else.
    /// </summary>
    public async Task<Result> EnsureCanRecordAttendanceAsync(Guid periodId, CancellationToken ct)
    {
        var serviceId = await dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId)
            .Select(p => (int?)p.ServiceId)
            .FirstOrDefaultAsync(ct);

        if (serviceId is null)
            return Result.Failure(StageErrors.PeriodNotFound(periodId));

        if (IsAdministrative)
            return Result.Success();

        var myServices = await MyServiceIdsAsync(ct);
        return myServices.Contains(serviceId.Value)
            ? Result.Success()
            : Result.Failure(StageErrors.AttendanceNotAllowed);
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
