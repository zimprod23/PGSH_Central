using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Calendar.Pauses;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;
using PGSH.Application.Students.Search;

namespace PGSH.Application.Stages.InternshipAssignments.GetMany;

internal sealed class GetInternshipAssignmentsQueryHandler(
    IApplicationDbContext dbContext,
    PromotionSuspensionLookup suspensions,
    IDateTimeProvider clock)
    : IQueryHandler<GetInternshipAssignmentsQuery, PaginatedResponse<InternshipAssignmentSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<InternshipAssignmentSummaryResponse>>> Handle(
        GetInternshipAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InternshipAssignments.AsNoTracking().AsQueryable();

        if (request.CohortIds is { Count: > 0 })
            query = query.Where(a => request.CohortIds.Contains(a.CurrentCohortId));

        if (request.RegistrationId.HasValue)
            query = query.Where(a => a.RegistrationId == request.RegistrationId.Value);

        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);

        if (request.StageId.HasValue)
            query = query.Where(a => a.Cohort.StageId == request.StageId.Value);

        if (request.PartitionLabels is { Count: > 0 })
            query = query.Where(a => a.Cohort.AcademicGroup.RotationGroup != null
                                  && request.PartitionLabels.Contains(a.Cohort.AcademicGroup.RotationGroup));

        if (request.PeriodNumber.HasValue)
            query = query.Where(a => a.ServicePeriods.Any(p =>
                p.CohortSlotAssignment != null
                && p.CohortSlotAssignment.StageSlot.PeriodNumber == request.PeriodNumber.Value));

        query = query.WhereStudentMatches(request.Search, a => a.Registration.Student);

        // ⚠ La promotion voyage jusqu'ici parce que la suspension se lit sur elle, et le magasin ne
        // peut pas la joindre : une fenêtre déclarée n'écrit rien sur la rotation. La ligne est donc
        // projetée avec son (année, niveau), puis la fenêtre du jour y est pliée en mémoire.
        var page = await query
            .OrderBy(a => a.Registration.Student.LastName)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                a => new Row(
                    a.Id,
                    a.RegistrationId,
                    (a.Registration.Student.FirstName ?? "") + " " + (a.Registration.Student.LastName ?? ""),
                    a.CurrentCohortId,
                    a.Cohort.Label,
                    a.Cohort.StageId,
                    a.Cohort.Stage.Name,
                    a.Status,
                    a.FinalScore,
                    a.Result,
                    a.ServicePeriods.Any(p => p.IsPaused),
                    a.ServicePeriods.Any(p => !p.IsInterrupted)
                        && a.ServicePeriods.All(p => p.IsInterrupted || p.Evaluation != null),
                    a.ServicePeriods.Any(p => p.IsDelocalized),
                    a.Registration.AcademicYearId,
                    a.Registration.LevelId),
                cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var windows = await suspensions.OnAsync(today, cancellationToken);

        var response = new PaginatedResponse<InternshipAssignmentSummaryResponse>(
            page.Items.Select(r => Map(r, windows)).ToList(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount);

        return Result.Success(response);
    }

    /// <remarks>
    /// ⚠ <b>La suspension ne s'applique qu'à une rotation en cours.</b> Une affectation seulement
    /// planifiée n'est nulle part, et une close appartient au passé : les afficher « En examens »
    /// répondrait à une question que personne ne pose et noierait les lignes où c'est vrai.
    /// </remarks>
    private static InternshipAssignmentSummaryResponse Map(
        Row r,
        IReadOnlyDictionary<(int AcademicYearId, int LevelId), PromotionSuspension> windows) =>
        new(r.Id,
            r.RegistrationId,
            r.StudentFullName,
            r.CohortId,
            r.CohortLabel,
            r.StageId,
            r.StageName,
            r.Status,
            r.FinalScore,
            r.Result,
            r.IsPaused,
            r.AllPeriodsEvaluated,
            r.IsDelocalized,
            r.Status == InternshipStatus.Ongoing
                ? windows.GetValueOrDefault((r.AcademicYearId, r.LevelId))
                : null);

    /// <summary>
    /// La ligne telle que le magasin la rend, plus la promotion — que la réponse ne porte pas et dont
    /// la suspension a besoin. Même raison que <c>GetPromotionPausesQueryHandler.PauseRow</c> : le
    /// sélecteur paginé est une <c>Expression</c> que le fournisseur doit traduire, et une fenêtre
    /// déclarée n'est jointe à rien.
    /// </summary>
    private sealed record Row(
        Guid Id,
        Guid RegistrationId,
        string StudentFullName,
        int CohortId,
        string CohortLabel,
        int StageId,
        string StageName,
        InternshipStatus Status,
        decimal? FinalScore,
        StageAssignmentResult? Result,
        bool IsPaused,
        bool AllPeriodsEvaluated,
        bool IsDelocalized,
        int AcademicYearId,
        int LevelId);
}
