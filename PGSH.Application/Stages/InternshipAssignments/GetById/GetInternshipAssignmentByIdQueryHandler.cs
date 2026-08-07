using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetById;

internal sealed class GetInternshipAssignmentByIdQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetInternshipAssignmentByIdQuery, InternshipAssignmentResponse>
{
    public async Task<Result<InternshipAssignmentResponse>> Handle(
        GetInternshipAssignmentByIdQuery request, CancellationToken cancellationToken)
    {
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
                        p.Delocalization != null ? p.Delocalization.Reason : null))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return assignment is null
            ? Result.Failure<InternshipAssignmentResponse>(StageErrors.AssignmentNotFound(request.Id))
            : assignment;
    }
}
