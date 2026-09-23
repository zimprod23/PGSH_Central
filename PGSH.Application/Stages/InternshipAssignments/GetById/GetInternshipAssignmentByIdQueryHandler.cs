using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Calendar.Pauses;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetById;

internal sealed class GetInternshipAssignmentByIdQueryHandler(
    IApplicationDbContext dbContext,
    PromotionSuspensionLookup suspensions,
    IDateTimeProvider clock)
    : IQueryHandler<GetInternshipAssignmentByIdQuery, InternshipAssignmentResponse>
{
    public async Task<Result<InternshipAssignmentResponse>> Handle(
        GetInternshipAssignmentByIdQuery request, CancellationToken cancellationToken)
    {
        // ⚠ La promotion est relevée à part : une fenêtre déclarée n'est jointe à aucune rotation, donc
        // le magasin ne peut pas la porter dans la projection.
        var promotion = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Id == request.Id)
            .Select(a => new
            {
                a.Registration.AcademicYearId,
                a.Registration.LevelId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        var assignment = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Id == request.Id)
            .Select(a => new InternshipAssignmentResponse(
                a.Id,
                a.RegistrationId,
                (a.Registration.Student.FirstName ?? "") + " " + (a.Registration.Student.LastName ?? ""),
                a.CurrentCohortId,
                a.Cohort.Label,
                a.Status,
                a.FinalScore,
                a.Result,
                a.ServicePeriods
                    .OrderBy(p => p.StartDate)
                    .Select(p => new ServicePeriodSummary(
                        p.Id,
                        p.ServiceId,
                        p.Service.Name,
                        p.Service.Hospital.Name,
                        p.StartDate,
                        p.EndDate,
                        p.IsComplete,
                        p.Evaluation != null,
                        p.IsStarted,
                        p.IsPaused,
                        p.Pauses.Where(x => x.ResumeDate == null).Select(x => x.Reason).FirstOrDefault(),
                        p.IsDelocalized,
                        p.Delocalization != null ? p.Delocalization.Reason : null,
                        // Explicite : un argument facultatif est interdit dans une arborescence
                        // d'expression, et la fenêtre est de toute façon pliée après la requête.
                        null))
                    .ToList(),
                // ⚠ Explicite, et non par défaut : une arborescence d'expression refuse un argument
                // facultatif. La fenêtre est pliée après la requête, comme celle des périodes.
                null))
            .SingleOrDefaultAsync(cancellationToken);

        if (assignment is null || promotion is null)
            return Result.Failure<InternshipAssignmentResponse>(StageErrors.AssignmentNotFound(request.Id));

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var windows = await suspensions.OnAsync(today, cancellationToken);

        var window = windows.GetValueOrDefault((promotion.AcademicYearId, promotion.LevelId));

        // ⚠ Posée seulement sur la rotation qui a *effectivement lieu aujourd'hui*, pas sur tout le
        // dossier. Deux conditions, et la seconde a failli manquer : `InternshipAssignment.Start()`
        // ouvre **toutes** les périodes d'un coup (« whole-student start »), si bien qu'un séjour de
        // mai porte `IsStarted` dès septembre. Sur le seul critère du cycle de vie, une fenêtre de mars
        // aurait donc marqué « En examens » un stage qui ne commence pas avant deux mois.
        return window is null
            ? assignment
            : assignment with
            {
                // ⚠ Deux portées, deux critères, et il faut les deux. L'affectation dit « cet étudiant
                // compose cette semaine » — vrai de tout le dossier tant qu'il est en cours, quelles
                // que soient les dates de tel séjour. La période dit « il n'est pas dans ce service ce
                // matin » — ce qui demande en plus que sa fenêtre contienne le jour. Ne poser que la
                // seconde laissait le badge du stage afficher « En cours » au-dessus de rotations
                // marquées « En examens » : un dossier qui se contredit à une ligne d'intervalle.
                SuspendedBy = assignment.Status == InternshipStatus.Ongoing ? window : null,

                ServicePeriods = assignment.ServicePeriods
                    .Select(p => IsRunningOn(p, today) ? p with { SuspendedBy = window } : p)
                    .ToList(),
            };
    }

    /// <summary>
    /// ⚠ <b>« Ouverte » et « en cours aujourd'hui » ne sont pas la même question.</b> La première est
    /// <c>ServicePeriodLifecycle.Underway</c> et vaut pour tout le séjour ; la seconde demande en plus
    /// que la fenêtre du séjour contienne le jour, ce qui est la seule façon de dire où l'étudiant est
    /// <i>ce matin</i>.
    /// </summary>
    private static bool IsRunningOn(ServicePeriodSummary period, DateOnly on) =>
        period.IsStarted
        && !period.IsComplete
        && period.StartDate <= on
        && period.EndDate >= on;
}
