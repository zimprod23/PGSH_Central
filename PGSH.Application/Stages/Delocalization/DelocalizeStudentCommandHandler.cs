using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization;

internal sealed class DelocalizeStudentCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<DelocalizeStudentCommand>
{
    public async Task<Result> Handle(DelocalizeStudentCommand request, CancellationToken cancellationToken)
    {
        // Scolarité only. A délocalisation drops the planned rotation, closes the stage and — when a
        // verdict is supplied — records the mark, all in one call. Left open to any authenticated
        // user, a student could post their own registrationId with outcome Validated and pass their
        // own stage. There is no in-app chef for an external service to scope this to, so the check
        // is on who you are.
        var access = authorizer.EnsureIsAdministrative(StageErrors.DelocalizationNotAllowed);
        if (access.IsFailure)
            return access;

        var registration = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.Id == request.RegistrationId)
            .Select(r => new { r.Id, r.AcademicGroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
            return Result.Failure(Error.NotFound(
                "Registrations.NotFound", $"Registration '{request.RegistrationId}' not found."));

        if (registration.AcademicGroupId is null)
            return Result.Failure(StageErrors.NoGroupForDelocalization);

        if (!await dbContext.Stages.AnyAsync(s => s.Id == request.StageId, cancellationToken))
            return Result.Failure(StageErrors.NotFound(request.StageId));

        // The external service must exist in the catalog (admin adds the out-of-faculty service first);
        // it is intentionally NOT constrained to the stage's allowed-services list.
        if (!await dbContext.Services.AnyAsync(s => s.Id == request.ServiceId, cancellationToken))
            return Result.Failure(Error.NotFound(
                "Services.NotFound", $"Service '{request.ServiceId}' not found."));

        var cohortId = await dbContext.Cohorts
            .Where(c => c.AcademicGroupId == registration.AcademicGroupId.Value && c.StageId == request.StageId)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (cohortId == 0)
            return Result.Failure(StageErrors.CohortMissingForStage(request.StageId));

        var assignment = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstOrDefaultAsync(
                a => a.RegistrationId == request.RegistrationId && a.Cohort.StageId == request.StageId,
                cancellationToken);

        bool isNew = assignment is null;
        if (assignment is null)
        {
            var assignmentId = Guid.NewGuid();
            assignment = new InternshipAssignment
            {
                Id              = assignmentId,
                RegistrationId  = request.RegistrationId,
                CurrentCohortId = cohortId,
            };
            assignment.MembershipHistory.Add(new CohortMembership
            {
                Id                     = Guid.NewGuid(),
                InternshipAssignmentId = assignmentId,
                CohortId               = cohortId,
                StartDate              = DateOnly.FromDateTime(DateTime.UtcNow),
            });
        }

        var result = assignment.Delocalize(
            request.StageId, request.ServiceId, request.StartDate, request.EndDate,
            request.Reason, request.DemandeId);

        if (result.IsFailure)
            return result;

        // The external service certifies on paper; when the verdict is already known, record it as a
        // validate-only evaluation in the same step so the délocalisation is fully closed out.
        if (request.Outcome is not null)
        {
            var period = assignment.ServicePeriods.Single();
            var evaluation = new ServiceEvaluation
            {
                ServicePeriodId = period.Id,
                Mode            = EvaluationMode.ValidatePeriod,
                Outcome         = request.Outcome,
                FicheReference  = request.FicheReference,
            };

            var evalResult = assignment.SubmitEvaluation(period.Id, evaluation);
            if (evalResult.IsFailure)
                return evalResult;
        }

        if (isNew)
            dbContext.InternshipAssignments.Add(assignment);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
