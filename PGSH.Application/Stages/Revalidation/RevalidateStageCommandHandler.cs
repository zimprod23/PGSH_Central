using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Revalidation;

internal sealed class RevalidateStageCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<RevalidateStageCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RevalidateStageCommand request, CancellationToken cancellationToken)
    {
        // Scolarité only, for the same reason délocalisation is: opening a stage decides what a student
        // has left to serve. Left to any authenticated caller, a student could re-open a stage they had
        // already failed and hand themselves a second run at it.
        var access = authorizer.EnsureIsAdministrative(StageErrors.RevalidationNotAllowed);
        if (access.IsFailure)
            return Result.Failure<Guid>(access.Error);

        var registration = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.Id == request.RegistrationId)
            .Select(r => new { r.Id, r.StudentId, r.AcademicGroupId })
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
            return Result.Failure<Guid>(Error.NotFound(
                "Registrations.NotFound", $"Registration '{request.RegistrationId}' not found."));

        if (!await dbContext.Stages.AnyAsync(s => s.Id == request.StageId, cancellationToken))
            return Result.Failure<Guid>(StageErrors.NotFound(request.StageId));

        // Deliberately NOT constrained to the registration's own level. A stage is not necessarily a
        // criterion for failing the year, so a student carries an unvalidated stage forward and may
        // still be redoing a 1st-year stage in their 6th year. The real constraint is the failed
        // attempt checked below — having one proves they were registered at that level once.

        bool alreadyOnThisRegistration = await dbContext.InternshipAssignments
            .AsNoTracking()
            .AnyAsync(a => a.RegistrationId == request.RegistrationId
                        && a.Cohort.StageId == request.StageId, cancellationToken);

        if (alreadyOnThisRegistration)
            return Result.Failure<Guid>(StageErrors.AlreadyAssignedForStage(request.StageId));

        // The whole point of a revalidation is that an earlier attempt failed. Look across every OTHER
        // registration this student holds — the failure and the retake are, by definition, different years.
        var priorAttempts = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Registration.StudentId == registration.StudentId
                     && a.Cohort.StageId == request.StageId
                     && a.RegistrationId != request.RegistrationId)
            .Select(a => new
            {
                a.RegistrationId,
                a.Result,
                // Where the student actually served it — the default destination for the retake.
                OriginalServiceId = a.ServicePeriods
                    .OrderByDescending(p => p.StartDate)
                    .Select(p => (int?)p.ServiceId)
                    .FirstOrDefault(),
                // Used to pick the most recent failure when there is more than one.
                LastServedOn = a.ServicePeriods.Max(p => (DateOnly?)p.StartDate),
            })
            .ToListAsync(cancellationToken);

        if (priorAttempts.Count == 0)
            return Result.Failure<Guid>(StageErrors.NothingToRevalidate(request.StageId));

        // A stage once acquired is never repeated, whichever year earned it.
        if (priorAttempts.Any(a => a.Result == StageAssignmentResult.Validé))
            return Result.Failure<Guid>(StageErrors.StageAlreadyValidated(request.StageId));

        // EVERY prior attempt must be settled, not merely one of them — the same test
        // DossierStageState uses to reach ToRevalidate. With `Any`, a student holding a 2022 failure
        // and a 2023 attempt still awaiting its verdict would get a retake opened alongside the live
        // one: two attempts running at once, and the pending one might yet come back validated. The
        // dossier would call that student InProgress while the command re-opened them.
        if (!priorAttempts.All(a => a.Result == StageAssignmentResult.NonValidé))
            return Result.Failure<Guid>(StageErrors.RevalidationStillOpen(request.StageId));

        var cohort = await ResolveCohortAsync(request, registration.AcademicGroupId, cancellationToken);
        if (cohort.IsFailure)
            return Result.Failure<Guid>(cohort.Error);

        int cohortId = cohort.Value;

        // The most recent failure decides where the retake goes. Taking whichever row the database
        // happened to return first would make "served where the student failed it" depend on query
        // order once there is more than one failed attempt.
        var failedAttempt = priorAttempts
            .Where(a => a.Result == StageAssignmentResult.NonValidé)
            .OrderByDescending(a => a.LastServedOn ?? DateOnly.MinValue)
            .First();

        // Placement is all-or-nothing: either the retake is put somewhere now, or it is left to be
        // scheduled. Half a window is a mistake, not an intention.
        //
        // Resolved only when asked for, never as a nullable success: Result<T>'s implicit operator
        // maps a null value to a FAILURE, so "no placement" cannot be expressed as Result<Placement?>.
        Placement? placement = null;
        if (request.PlacesRotation)
        {
            var resolved = await ResolvePlacementAsync(request, failedAttempt.OriginalServiceId, cancellationToken);
            if (resolved.IsFailure)
                return Result.Failure<Guid>(resolved.Error);

            placement = resolved.Value;
        }

        var assignmentId = Guid.NewGuid();
        var assignment = new InternshipAssignment
        {
            Id              = assignmentId,
            RegistrationId  = request.RegistrationId,
            CurrentCohortId = cohortId,
        };

        // Brand-new graph passed to Add below, so pre-set keys are safe here — Add marks the whole
        // graph Added regardless of key values.
        assignment.MembershipHistory.Add(new CohortMembership
        {
            Id                     = Guid.NewGuid(),
            InternshipAssignmentId = assignmentId,
            CohortId               = cohortId,
            StartDate              = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        // A revalidation is served outside the published schedule — CohortSlotAssignmentId stays null,
        // the same meaning a délocalisation relies on. It is one student making good one stage, not a
        // cell in their group's rotation grid.
        if (placement is { } window)
        {
            assignment.ServicePeriods.Add(new ServicePeriod
            {
                InternshipAssignmentId = assignmentId,
                ServiceId              = window.ServiceId,
                CohortSlotAssignmentId = null,
                StartDate              = window.Start,
                EndDate                = window.End,
            });
        }

        assignment.Raise(new StageRevalidationOpenedDomainEvent(
            assignmentId,
            request.RegistrationId,
            failedAttempt.RegistrationId,
            request.StageId,
            cohortId,
            request.Reason));

        dbContext.InternshipAssignments.Add(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return assignmentId;
    }

    private sealed record Placement(int ServiceId, DateOnly Start, DateOnly End);

    /// <summary>
    /// Which service the retake is served in, and when. The default is the service the student failed
    /// it in — a revalidation sends them back where they were, not wherever this year's grid would put
    /// their group. An explicit <c>ServiceId</c> overrides that, which per the faculty's rule is itself
    /// a change subject to an approved demande.
    /// </summary>
    private async Task<Result<Placement>> ResolvePlacementAsync(
        RevalidateStageCommand request, int? originalServiceId, CancellationToken cancellationToken)
    {
        if (request.StartDate is not { } start || request.EndDate is not { } end || end < start)
            return Result.Failure<Placement>(StageErrors.IncompletePlacement);

        int? serviceId = request.ServiceId ?? originalServiceId;
        if (serviceId is null)
            return Result.Failure<Placement>(StageErrors.OriginalServiceUnknown);

        if (!await dbContext.Services.AnyAsync(s => s.Id == serviceId.Value, cancellationToken))
            return Result.Failure<Placement>(Error.NotFound(
                "Services.NotFound", $"Service '{serviceId.Value}' not found."));

        return new Placement(serviceId.Value, start, end);
    }

    /// <summary>
    /// Where the retake is served. An explicit cohort wins — that is how a stage from an earlier level
    /// is rejoined, by slotting the student into a cohort currently running it. Otherwise fall back to
    /// the student's own group, which is what the repeating-the-same-year case needs.
    /// </summary>
    private async Task<Result<int>> ResolveCohortAsync(
        RevalidateStageCommand request, int? academicGroupId, CancellationToken cancellationToken)
    {
        if (request.CohortId is not null)
        {
            var target = await dbContext.Cohorts
                .AsNoTracking()
                .Where(c => c.Id == request.CohortId.Value)
                .Select(c => new { c.Id, c.StageId })
                .FirstOrDefaultAsync(cancellationToken);

            if (target is null)
                return Result.Failure<int>(StageErrors.CohortNotFound(request.CohortId.Value));

            return target.StageId == request.StageId
                ? target.Id
                : Result.Failure<int>(StageErrors.CohortNotForStage(request.CohortId.Value, request.StageId));
        }

        if (academicGroupId is null)
            return Result.Failure<int>(StageErrors.NoGroupForRevalidation);

        int ownCohortId = await dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.AcademicGroupId == academicGroupId.Value && c.StageId == request.StageId)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return ownCohortId == 0
            ? Result.Failure<int>(StageErrors.NoCohortForRevalidation(request.StageId))
            : ownCohortId;
    }
}
