using System.Text.Json;
using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Delocalization.Bulk;

/// <summary>
/// Délocalise a selection of students on one stage: whole rosters, named students, or a list pasted
/// from the form the faculty circulated — in any combination.
/// </summary>
/// <remarks>
/// <para><b>Skips what it cannot do, and never silently.</b> A student whose stage already carries a
/// mark is refused, named on the report, and the others are still written. Refusing the whole batch
/// was the alternative and it is worse here: a roster is selected by one id, so a single refused
/// member would block a promotion the operator cannot edit member by member — and « corrige la liste
/// et recommence » is exactly how a real list stops being re-run at all.</para>
///
/// <para>⚠ <b>What guards it is <see cref="ConfirmedCount"/>, not a checkbox.</b> The act lands on
/// students nobody typed the name of; a registration created, transferred or evaluated between the
/// preview and the apply changes the outcome without changing anything the operator saw. The number
/// he was shown is sent back and a mismatch refuses.</para>
///
/// <para>⚠ <b>It does not touch the planning grid.</b> The cells stay where they are and simply stop
/// counting the students who left — <c>ServiceOccupancyCalculator</c> excludes a délocalisé from the
/// load of the cohorte he is still a member of. That is what makes the act reversible: cancelling a
/// délocalisation and re-publishing restores the rotation, which deleting the cells would not.</para>
///
/// <para>⚠ <b>Expect the write to take a while on a whole promotion.</b> Each student raises
/// <c>StudentDelocalizedDomainEvent</c> and <c>ApplicationDbContext</c> publishes events after the
/// commit, one at a time, each writing a <c>History</c> row. The transaction itself is quick; the
/// dossier entries appearing afterwards are progress, not a hang.</para>
/// </remarks>
/// <param name="ConfirmedCount">The number of students the preview said would be délocalisés.</param>
public sealed record ApplyBulkDelocalizationCommand(
    int StageId,
    int ServiceId,
    string Reason,
    DelocalizationTargets Targets,
    int ConfirmedCount,
    int? AcademicYearId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null) : ICommand<BulkDelocalizationReport>, IAuditableCommand
{
    public string  AuditAction     => "BULK_DELOCALIZATION_APPLIED";
    public string  AuditEntityType => "Stage";
    public string? AuditEntityId   => StageId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        serviceId       = ServiceId,
        academicYearId  = AcademicYearId,
        reason          = Reason,
        confirmedCount  = ConfirmedCount,
        groupIds        = Targets.AcademicGroupIds,
        registrationIds = Targets.RegistrationIds?.Count,
        identifiers     = Targets.Identifiers?.Count,
    });
}

internal sealed class ApplyBulkDelocalizationCommandValidator
    : AbstractValidator<ApplyBulkDelocalizationCommand>
{
    public ApplyBulkDelocalizationCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleFor(x => x.ServiceId).GreaterThan(0);
        RuleFor(x => x.Targets).NotNull();
        RuleFor(x => x.ConfirmedCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Un motif est requis pour la délocalisation.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.EndDate is not null)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");
    }
}

internal sealed class ApplyBulkDelocalizationCommandHandler(
    IApplicationDbContext dbContext,
    BulkDelocalizationPlanner planner,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyBulkDelocalizationCommand, BulkDelocalizationReport>
{
    public async Task<Result<BulkDelocalizationReport>> Handle(
        ApplyBulkDelocalizationCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BulkDelocalizationErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<BulkDelocalizationReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.StageId, request.ServiceId, request.AcademicYearId, request.Targets,
            request.StartDate, request.EndDate, cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<BulkDelocalizationReport>(plan.Error);

        // ⚠ Before anything is written, and against the plan's own count rather than the report's
        // — they are the same number, and asking the collection that is about to be executed is the
        // one that cannot drift from what runs.
        if (request.ConfirmedCount != plan.Value.Work.Count)
            return Result.Failure<BulkDelocalizationReport>(
                BulkDelocalizationErrors.CountMismatch(request.ConfirmedCount, plan.Value.Work.Count));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var item in plan.Value.Work)
        {
            var assignment = item.Assignment;
            bool isNew = assignment is null;

            assignment ??= DelocalizationAssignmentFactory.CreateFor(
                item.RegistrationId, item.CohortId, today);

            // Through the aggregate, one student at a time. The planner already refused every case
            // Delocalize refuses, so a failure here is the two disagreeing — which is worth failing
            // the whole act over rather than writing part of a list somebody confirmed.
            var result = assignment.Delocalize(
                plan.Value.StageId, plan.Value.ServiceId,
                plan.Value.StartDate, plan.Value.EndDate, request.Reason, demandeId: null);

            if (result.IsFailure)
                return Result.Failure<BulkDelocalizationReport>(result.Error);

            if (isNew)
                dbContext.InternshipAssignments.Add(assignment);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return plan.Value.Report;
    }
}
