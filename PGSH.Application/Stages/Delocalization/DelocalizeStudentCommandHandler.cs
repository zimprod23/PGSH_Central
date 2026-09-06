using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization;

internal sealed class DelocalizeStudentCommandHandler(
    IApplicationDbContext dbContext,
    DelocalizationVerdictWriter verdictWriter,
    AcademicYearResolver yearResolver,
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
            .Select(r => new { r.Id, r.AcademicGroupId, r.AcademicYearId })
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
            return Result.Failure(RegistrationErrors.NotFound(request.RegistrationId));

        if (registration.AcademicGroupId is null)
            return Result.Failure(StageErrors.NoGroupForDelocalization);

        string? stageName = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == request.StageId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (stageName is null)
            return Result.Failure(StageErrors.NotFound(request.StageId));

        // The external service must exist in the catalog (admin adds the out-of-faculty service first);
        // it is intentionally NOT constrained to the stage's allowed-services list.
        if (!await dbContext.Services.AnyAsync(s => s.Id == request.ServiceId, cancellationToken))
            return Result.Failure(ServiceErrors.NotFound(request.ServiceId));

        // ⚠ The registration's year, never the current one. A délocalisation is recorded against the
        // registration it belongs to, and scolarité enters last year's papers well into the next
        // year — resolving « the year in progress » here would date the stage to a promotion the
        // student is no longer in and read its window off the wrong grid.
        var window = await ResolveWindowAsync(
            request, registration.AcademicYearId, stageName, cancellationToken);

        if (window.IsFailure)
            return Result.Failure(window.Error);

        var cohortId = await dbContext.Cohorts
            .Where(c => c.AcademicGroupId == registration.AcademicGroupId.Value && c.StageId == request.StageId)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (cohortId == 0)
            return Result.Failure(StageErrors.CohortMissingForStage(request.StageId));

        // ⚠ The evaluations are Included because the guard inside Delocalize reads them. Left out,
        // every period looks unmarked, the refusal never fires and the délocalisation deletes the
        // mark it exists to protect — the in-memory suite would not see it either, since it fixes
        // navigations up from the change tracker.
        var assignment = await dbContext.InternshipAssignments
            .Include(a => a.ServicePeriods)
                .ThenInclude(p => p.Evaluation)
            .FirstOrDefaultAsync(
                a => a.RegistrationId == request.RegistrationId && a.Cohort.StageId == request.StageId,
                cancellationToken);

        bool isNew = assignment is null;
        assignment ??= DelocalizationAssignmentFactory.CreateFor(
            request.RegistrationId, cohortId, DateOnly.FromDateTime(DateTime.UtcNow));

        var (start, end) = window.Value;

        var result = assignment.Delocalize(
            request.StageId, request.ServiceId, start, end, request.Reason, request.DemandeId);

        if (result.IsFailure)
            return result;

        // The external service certifies on paper; when the verdict is already in hand, record it in
        // the same step so the délocalisation is fully closed out. Whatever form it arrived in — a
        // note, a pass/fail, a fiche ticked objective by objective — goes through the same writer as
        // a chef's own evaluation.
        if (request.Verdict is { } verdict)
        {
            var period = assignment.ServicePeriods.Single();
            var written = await verdictWriter.WriteAsync(
                assignment, period, request.StageId, verdict, cancellationToken);

            if (written.IsFailure)
                return written;
        }

        if (isNew)
            dbContext.InternshipAssignments.Add(assignment);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<(DateOnly Start, DateOnly End)>> ResolveWindowAsync(
        DelocalizeStudentCommand request, int academicYearId, string stageName, CancellationToken ct)
    {
        if (request is { StartDate: { } start, EndDate: { } end })
            return Result.Success((start, end));

        var year = await yearResolver.ResolveWithLabelAsync(academicYearId, ct);

        return year.IsFailure
            ? Result.Failure<(DateOnly, DateOnly)>(year.Error)
            : await DelocalizationWindow.ResolveAsync(
                dbContext, request.StageId, academicYearId, stageName, year.Value.Label, ct);
    }
}
