using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Calendar.Pauses;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Extensions;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.ServicePeriods.GetMany;

internal sealed class GetServicePeriodsQueryHandler(
    IApplicationDbContext dbContext,
    IUserContext userContext,
    ExecutionAuthorizer authorizer,
    PromotionSuspensionLookup suspensions,
    IDateTimeProvider clock)
    : IQueryHandler<GetServicePeriodsQuery, PaginatedResponse<ServicePeriodResponse>>
{
    public async Task<Result<PaginatedResponse<ServicePeriodResponse>>> Handle(
        GetServicePeriodsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ServicePeriods.AsNoTracking().AsQueryable();

        // Global administrative staff read across every service. A service-scoped user
        // (chef or secretary staffing a service) reads only his own services' periods —
        // this is the shared read behind the presence grid. Anyone else is refused.
        if (!Roles.Administrative.Any(userContext.IsInRole))
        {
            var myServices = await authorizer.MyServiceIdsAsync(cancellationToken);
            if (myServices.Count == 0)
                return Result.Failure<PaginatedResponse<ServicePeriodResponse>>(StageErrors.AdministrativeOnly);

            query = query.Where(p => myServices.Contains(p.ServiceId));
        }

        if (request.AssignmentId.HasValue)
            query = query.Where(p => p.InternshipAssignmentId == request.AssignmentId.Value);

        if (request.ServiceId.HasValue)
            query = query.Where(p => p.ServiceId == request.ServiceId.Value);

        if (request.CohortId.HasValue)
            query = query.Where(p => p.InternshipAssignment.CurrentCohortId == request.CohortId.Value);

        if (request.IsComplete.HasValue)
            query = query.Where(p => p.IsComplete == request.IsComplete.Value);

        if (request.AcademicYearId.HasValue)
            query = query.Where(p => p.InternshipAssignment.Registration.AcademicYearId == request.AcademicYearId.Value);

        // ⚠ Projeté vers une ligne intermédiaire plutôt que droit vers la réponse : la suspension se
        // lit sur la promotion, qu'une fenêtre déclarée ne joint à rien, et la réponse ne la porte pas.
        // Même forme que GetInternshipAssignmentsQueryHandler et que la liste des fenêtres.
        var page = await query
            .OrderBy(p => p.StartDate)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                p => new Row(
                    p.Id,
                    p.InternshipAssignmentId,
                    (p.InternshipAssignment.Registration.Student.FirstName ?? "") + " " +
                    (p.InternshipAssignment.Registration.Student.LastName ?? ""),
                    p.InternshipAssignment.Registration.Student.CNE,
                    p.InternshipAssignment.Registration.Student.Appogee,
                    p.ServiceId,
                    p.Service.Name,
                    p.Service.Hospital.Name,
                    p.StartDate,
                    p.EndDate,
                    p.IsComplete,
                    p.Evaluation != null,
                    p.InternshipAssignment.Cohort.AcademicGroup.Label,
                    p.InternshipAssignment.Cohort.Stage.Name,
                    p.InternshipAssignment.Cohort.Stage.Level.Label,
                    null,
                    p.IsPaused,
                    p.Pauses.Where(x => x.ResumeDate == null).Select(x => x.Reason).FirstOrDefault(),
                    p.IsInterrupted,
                    // The four facts are translated; the decision over them is made client-side, which
                    // is what EF does with a top-level projection anyway. Restating the rule inline
                    // here would be the fifth copy of exactly what ServicePeriodLifecycle removed.
                    ServicePeriodLifecycle.StateOf(
                        p.IsStarted, p.IsComplete, p.IsInterrupted, p.Evaluation != null),
                    p.InternshipAssignment.Registration.AcademicYearId,
                    p.InternshipAssignment.Registration.LevelId),
                cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var windows = await suspensions.OnAsync(today, cancellationToken);

        var response = new PaginatedResponse<ServicePeriodResponse>(
            page.Items.Select(r => new ServicePeriodResponse(
                r.Id, r.InternshipAssignmentId, r.StudentFullName, r.StudentCne, r.StudentAppogee,
                r.ServiceId, r.ServiceName, r.HospitalName, r.StartDate, r.EndDate, r.IsComplete,
                r.HasEvaluation, r.AcademicGroupLabel, r.StageName, r.LevelLabel, r.Transfer,
                r.IsPaused, r.PauseReason, r.IsInterrupted, r.State,
                // ⚠ Deux conditions : ouverte, **et** sa fenêtre contient le jour. Start() ouvre
                // toutes les périodes d'un coup, donc le seul état du cycle de vie marquerait « En
                // examens » un séjour qui ne commence que dans deux mois.
                r.State == ServicePeriodState.Underway
                    && r.StartDate <= today && r.EndDate >= today
                    ? windows.GetValueOrDefault((r.AcademicYearId, r.LevelId))
                    : null)).ToList(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount);

        return Result.Success(response);
    }

    /// <summary>
    /// La ligne telle que le magasin la rend, plus la promotion — voir le commentaire ci-dessus.
    /// </summary>
    private sealed record Row(
        Guid Id,
        Guid InternshipAssignmentId,
        string StudentFullName,
        string? StudentCne,
        string StudentAppogee,
        int ServiceId,
        string ServiceName,
        string HospitalName,
        DateOnly StartDate,
        DateOnly EndDate,
        bool IsComplete,
        bool HasEvaluation,
        string AcademicGroupLabel,
        string StageName,
        string? LevelLabel,
        TransferMarker? Transfer,
        bool IsPaused,
        string? PauseReason,
        bool IsInterrupted,
        ServicePeriodState State,
        int AcademicYearId,
        int LevelId);
}
