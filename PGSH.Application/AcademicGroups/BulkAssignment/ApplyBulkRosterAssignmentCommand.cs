using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicGroups.GroupChange;
using PGSH.Application.Audit;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Students.Selection;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.BulkAssignment;

/// <summary>
/// Puts a named list of students into one roster: whole rosters, named students, or a list pasted
/// from the form the faculty circulated — in any combination.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A partner hospital takes the students who volunteered for it, and the
/// answer to a nominative placement request is a <b>roster</b> — not a transfer, not a
/// délocalisation. Composing that roster was one dialog per student, so a list of a hundred was a
/// hundred dialogs. Everything downstream — the pinned cells, the printed ranges — already worked;
/// this is the step that was done by hand.</para>
///
/// <para><b>Skips what it cannot do, and never silently.</b> A student whose rotation has begun is
/// refused, named on the report, and the others are still written. Refusing the whole batch was the
/// alternative and it is worse here: a list of volunteers is not something the operator can edit
/// member by member, and « corrige la liste et recommence » is how a real list stops being re-run at
/// all.</para>
///
/// <para>⚠ <b>What guards it is <see cref="ConfirmedCount"/>, not a checkbox.</b> The act lands on
/// students nobody typed the name of; a registration created, transferred or evaluated between the
/// preview and the apply changes the outcome without changing anything the operator saw. The number
/// he was shown is sent back, and a mismatch refuses.</para>
///
/// <para>⚠ <b>It moves students, it does not place them anywhere.</b> Which service the roster goes
/// to is the planning grid's answer — a pinned cell on a reserved service. Doing both here would
/// make one act that changes a roster's membership <i>and</i> writes the plan, and the second half
/// has guards, an audit entry and a published-cells refusal of its own.</para>
/// </remarks>
/// <param name="ConfirmedCount">The number of students the preview said would be affected.</param>
public sealed record ApplyBulkRosterAssignmentCommand(
    int TargetGroupId,
    StudentTargets Targets,
    int ConfirmedCount,
    string? Reason = null,
    int? AcademicYearId = null) : ICommand<BulkRosterAssignmentReport>, IAuditableCommand
{
    public string  AuditAction     => "STUDENTS_ASSIGNED_TO_ROSTER";
    public string  AuditEntityType => "AcademicGroup";
    public string? AuditEntityId   => TargetGroupId.ToString();

    // Through AuditMetadataJson rather than interpolated: the reason is free text somebody typed, so
    // a quote in it would write metadata that is not JSON — in the one column whose whole purpose is
    // to be read back later.
    public string? AuditMetadata => AuditMetadataJson.Of(
        ("academicYearId", AcademicYearId),
        ("confirmedCount", ConfirmedCount),
        ("groupIds", Targets.AcademicGroupIds is null ? null : string.Join(",", Targets.AcademicGroupIds)),
        ("registrationIds", Targets.RegistrationIds?.Count),
        ("identifiers", Targets.Identifiers?.Count),
        ("reason", Reason));
}

internal sealed class ApplyBulkRosterAssignmentCommandValidator
    : AbstractValidator<ApplyBulkRosterAssignmentCommand>
{
    public ApplyBulkRosterAssignmentCommandValidator()
    {
        RuleFor(x => x.TargetGroupId).GreaterThan(0);
        RuleFor(x => x.Targets).NotNull();
        RuleFor(x => x.ConfirmedCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class ApplyBulkRosterAssignmentCommandHandler(
    IApplicationDbContext dbContext,
    BulkRosterAssignmentPlanner planner,
    StudentGroupRelocator relocator,
    StudentAffectationService affectation,
    LateArrivalScheduler scheduler,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyBulkRosterAssignmentCommand, BulkRosterAssignmentReport>
{
    public async Task<Result<BulkRosterAssignmentReport>> Handle(
        ApplyBulkRosterAssignmentCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BulkRosterAssignmentErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<BulkRosterAssignmentReport>(access.Error);

        if (request.Targets.NamesNobody)
            return Result.Failure<BulkRosterAssignmentReport>(BulkRosterAssignmentErrors.NamesNobody);

        var plan = await planner.PlanAsync(
            request.TargetGroupId, request.AcademicYearId, request.Targets, cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<BulkRosterAssignmentReport>(plan.Error);

        // ⚠ Before anything is written, and against the plan's own collection rather than the
        // report's count — they are the same number, and asking the list that is about to be
        // executed is the one that cannot drift from what runs.
        if (request.ConfirmedCount != plan.Value.Work.Count)
            return Result.Failure<BulkRosterAssignmentReport>(
                BulkRosterAssignmentErrors.CountMismatch(request.ConfirmedCount, plan.Value.Work.Count));

        foreach (var item in plan.Value.Work)
        {
            var written = item.Joins
                ? await JoinAsync(item.RegistrationId, plan.Value.TargetGroupId, request.Reason, cancellationToken)
                : await MoveAsync(item.RegistrationId, plan.Value.TargetGroupId, cancellationToken);

            // The planner already refused every case the two single acts refuse, so a failure here is
            // the two disagreeing — worth failing the whole act over rather than writing part of a
            // list somebody confirmed. One transaction, so nothing of it survives.
            if (written.IsFailure)
                return Result.Failure<BulkRosterAssignmentReport>(written.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return plan.Value.Report;
    }

    /// <summary>
    /// A registration in no roster: attached, and its affectations created from the target's
    /// cohortes — the same two services « affecter à un groupe » runs, in the same order.
    /// </summary>
    private async Task<Result> JoinAsync(
        Guid registrationId, int targetGroupId, string? reason, CancellationToken ct)
    {
        var registration = await dbContext.Registrations
            .FirstOrDefaultAsync(r => r.Id == registrationId, ct);

        if (registration is null)
            return Result.Failure(RegistrationErrors.NotFound(registrationId));

        registration.TransferToGroup(targetGroupId, reason);

        var created = await affectation.AssignRegistrationAsync(registration, targetGroupId, ct);

        // A stage whose window has already closed is not materialised — the scheduler reports it
        // instead. That number is per student and belongs to the single act's report; here the
        // decision it feeds is the same for everybody and is taken before the list is sent.
        await scheduler.MaterializeRemainingAsync(created, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        return Result.Success();
    }

    /// <summary>
    /// A registration already in a roster: moved without trace, through the same relocator the
    /// single « changement de groupe » and the échange both run.
    /// </summary>
    /// <remarks>
    /// ⚠ The relocator deliberately does not save — an échange is two of these and they land
    /// together or not at all. That is exactly what this act needs: N moves, one transaction.
    /// </remarks>
    private async Task<Result> MoveAsync(Guid registrationId, int targetGroupId, CancellationToken ct)
    {
        var moved = await relocator.RelocateAsync(registrationId, targetGroupId, ct);
        return moved.IsFailure ? Result.Failure(moved.Error) : Result.Success();
    }
}
