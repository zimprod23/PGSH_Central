using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Revalidation;

/// <summary>
/// Whether a stage may be re-opened for a student, and what the retake would inherit.
///
/// <para>Shared by <see cref="RevalidateStageCommandHandler"/> and
/// <see cref="GetRevalidationContextQueryHandler"/> so that <b>the preview is the decision</b> — the
/// same guarantee the évaluation import and <c>CnpnTargetPlanner</c> make. A screen that showed
/// « ouvrable » from one set of rules while the command refused on another would be worse than no
/// screen: the refusal would arrive after the operator had committed to it.</para>
/// </summary>
internal static class RevalidationPlanner
{
    /// <summary>One earlier attempt at this stage, on a registration other than the one in hand.</summary>
    internal sealed record PriorAttempt(
        Guid RegistrationId,
        StageAssignmentResult? Result,
        int? OriginalServiceId,
        DateOnly? LastServedOn);

    /// <summary>
    /// Every earlier attempt at this stage, across all this student's <em>other</em> registrations.
    /// The failure and the retake are, by definition, different years.
    /// </summary>
    /// <remarks>
    /// Named and <c>internal static</c> for the usual reason: a query built inside a private async
    /// method cannot be handed to <c>ToQueryString()</c>, and the in-memory provider translates
    /// nothing. Pinned by <c>SqlTranslationTests</c>.
    /// </remarks>
    internal static IQueryable<PriorAttempt> PriorAttemptsQuery(
        IApplicationDbContext dbContext, Guid studentId, int stageId, Guid excludingRegistrationId) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Registration.StudentId == studentId
                     && a.Cohort.StageId == stageId
                     && a.RegistrationId != excludingRegistrationId)
            .Select(a => new PriorAttempt(
                a.RegistrationId,
                a.Result,
                // Where the student actually served it — the default destination for the retake.
                a.ServicePeriods
                    .OrderByDescending(p => p.StartDate)
                    .Select(p => (int?)p.ServiceId)
                    .FirstOrDefault(),
                // Used to pick the most recent failure when there is more than one.
                a.ServicePeriods.Max(p => (DateOnly?)p.StartDate)));

    /// <summary>Is this stage already open on the registration in hand?</summary>
    internal static IQueryable<Guid> ExistingAssignmentQuery(
        IApplicationDbContext dbContext, Guid registrationId, int stageId) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.RegistrationId == registrationId && a.Cohort.StageId == stageId)
            .Select(a => a.Id);

    /// <summary>
    /// The four rules, in the order their refusals are most useful. Returns the same
    /// <see cref="Error"/>s the command has always returned, because they are now the command's.
    /// </summary>
    internal static Result CheckEligibility(
        IReadOnlyList<PriorAttempt> priorAttempts, bool alreadyOnThisRegistration, int stageId)
    {
        if (alreadyOnThisRegistration)
            return Result.Failure(StageErrors.AlreadyAssignedForStage(stageId));

        // The whole point of a revalidation is that an earlier attempt failed.
        if (priorAttempts.Count == 0)
            return Result.Failure(StageErrors.NothingToRevalidate(stageId));

        // A stage once acquired is never repeated, whichever year earned it.
        if (priorAttempts.Any(a => a.Result == StageAssignmentResult.Validé))
            return Result.Failure(StageErrors.StageAlreadyValidated(stageId));

        // EVERY prior attempt must be settled, not merely one of them. With `Any`, a student holding
        // a 2022 failure and a 2023 attempt still awaiting its verdict would get a retake opened
        // alongside the live one — and the pending one might yet come back validated.
        if (!priorAttempts.All(a => a.Result == StageAssignmentResult.NonValidé))
            return Result.Failure(StageErrors.RevalidationStillOpen(stageId));

        return Result.Success();
    }

    /// <summary>
    /// The failure the retake is served against. Taking whichever row the database happened to
    /// return first would make "served where the student failed it" depend on query order.
    /// </summary>
    internal static PriorAttempt? LastFailure(IReadOnlyList<PriorAttempt> priorAttempts) =>
        priorAttempts
            .Where(a => a.Result == StageAssignmentResult.NonValidé)
            .OrderByDescending(a => a.LastServedOn ?? DateOnly.MinValue)
            .FirstOrDefault();
}
