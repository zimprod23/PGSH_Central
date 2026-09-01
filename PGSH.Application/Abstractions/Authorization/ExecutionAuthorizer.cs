using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Abstractions.Authorization;

/// <summary>
/// Single source of truth for "each chef controls only his own services". A service
/// period (and its evaluation) may be acted on by an administrative role or by the
/// chef of that period's service — nobody else. Shared by the execution write handlers
/// so the rule lives in one place.
///
/// <para>⚠ <b>It lived in <c>Employees/MyServices/</c> until 2026-09-01, and 48 files outside
/// <c>Employees/</c> imported it</b> — every CNPN, stage, group and academic-year handler among
/// them, each carrying a <c>using PGSH.Application.Employees.MyServices;</c> that said nothing
/// about what it needed. A cross-cutting rule filed inside one feature folder reads as that
/// feature's, which is how the next person comes to write a second copy rather than reach across
/// for this one. It answers « qui a le droit d'agir ici ? » for the whole application, so it sits
/// with the other abstractions.</para>
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
    /// Restricts an act to the administration (Scolarité / SuperUser). For operations that are neither
    /// scoped to a service nor owned by a student — recording a délocalisation, say, where there is no
    /// in-app chef to answer for the rotation and the only check left is who you are.
    /// </summary>
    public Result EnsureIsAdministrative(Error denied) =>
        IsAdministrative ? Result.Success() : Result.Failure(denied);

    /// <summary>
    /// Read scoping for presence, which is deliberately wider than the write scope: everyone who may
    /// record it, plus the student whose own rotation it is. Consulting your own attendance is not a
    /// privileged act — gating it behind the recording scope made the student portal fire a 403 on
    /// every stage it displayed.
    /// </summary>
    public Task<Result> EnsureCanReadAttendanceAsync(Guid periodId, CancellationToken ct) =>
        EnsureCanReadPeriodAsync(periodId, StageErrors.AttendanceNotAllowed, ct);

    /// <summary>
    /// Read scoping for a period's evaluation: the same people who may read its attendance. Reading
    /// your own mark is not privileged; reading a classmate's is.
    /// </summary>
    public Task<Result> EnsureCanReadEvaluationAsync(Guid periodId, CancellationToken ct) =>
        EnsureCanReadPeriodAsync(periodId, StageErrors.EvaluationReadNotAllowed, ct);

    // Everyone who may act on the period, plus the student it belongs to.
    private async Task<Result> EnsureCanReadPeriodAsync(Guid periodId, Error denied, CancellationToken ct)
    {
        var canRecord = await EnsureCanRecordAttendanceAsync(periodId, ct);
        if (canRecord.IsSuccess)
            return canRecord;

        // Not found outranks forbidden: keep the original error when the period does not exist.
        if (canRecord.Error == StageErrors.PeriodNotFound(periodId))
            return canRecord;

        var currentUserId = await CurrentEmployeeIdAsync(ct);
        if (currentUserId is null)
            return Result.Failure(denied);

        bool ownsPeriod = await dbContext.ServicePeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId
                        && p.InternshipAssignment.Registration.StudentId == currentUserId.Value, ct);

        return ownsPeriod ? Result.Success() : Result.Failure(denied);
    }

    /// <summary>
    /// Read scoping for a whole stage record — the marks, comments and attendance of one student's
    /// stage. Readable by the administration, by a chef or staff member of a service the student
    /// actually rotates through, and by the student themselves. Without this the ids are guessable
    /// and every classmate's file is readable.
    /// </summary>
    public async Task<Result> EnsureCanReadAssignmentAsync(Guid assignmentId, CancellationToken ct)
    {
        // Existence and ownership are separate questions: an assignment whose owner cannot be resolved
        // still exists, and answering "not found" for it would hide a real row behind a lie.
        var head = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Id == assignmentId)
            .Select(a => new { OwnerId = (Guid?)a.Registration.StudentId })
            .FirstOrDefaultAsync(ct);

        if (head is null)
            return Result.Failure(StageErrors.AssignmentNotFound(assignmentId));

        if (IsAdministrative)
            return Result.Success();

        var currentUserId = await CurrentEmployeeIdAsync(ct);
        if (currentUserId is null)
            return Result.Failure(StageErrors.AssignmentReadNotAllowed);

        if (head.OwnerId == currentUserId)
            return Result.Success();

        var myServices = await MyServiceIdsAsync(ct);
        if (myServices.Count == 0)
            return Result.Failure(StageErrors.AssignmentReadNotAllowed);

        bool rotatesThroughMyService = await dbContext.ServicePeriods
            .AsNoTracking()
            .AnyAsync(p => p.InternshipAssignmentId == assignmentId && myServices.Contains(p.ServiceId), ct);

        return rotatesThroughMyService
            ? Result.Success()
            : Result.Failure(StageErrors.AssignmentReadNotAllowed);
    }

    /// <summary>
    /// Read scoping for a student's whole level dossier — every registration at one level and every
    /// stage attempt under them. Deliberately tighter than <see cref="EnsureCanReadAssignmentAsync"/>:
    /// a chef may read the record of a stage a student actually served in his service, but the dossier
    /// exposes years of failures and retakes across the level, which is scolarité business. The
    /// administration and the student themselves — nobody else.
    /// </summary>
    public async Task<Result> EnsureCanReadStudentDossierAsync(Guid studentId, CancellationToken ct)
    {
        if (IsAdministrative)
            return Result.Success();

        var currentUserId = await CurrentEmployeeIdAsync(ct);
        return currentUserId is not null && currentUserId == studentId
            ? Result.Success()
            : Result.Failure(StageErrors.DossierReadNotAllowed);
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

    /// <summary>
    /// The same rule as <see cref="EnsureCanActOnPeriodAsync"/>, resolved once for a whole batch:
    /// <c>null</c> means the caller may evaluate any service (administrative), otherwise the set of
    /// services they lead. A bulk import checks hundreds of rotations — asking per period would be
    /// hundreds of round-trips for an answer that cannot change mid-request.
    /// </summary>
    public async Task<IReadOnlySet<int>?> EvaluableServiceIdsAsync(CancellationToken ct) =>
        IsAdministrative ? null : (await ChefServiceIdsAsync(ct)).ToHashSet();

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
