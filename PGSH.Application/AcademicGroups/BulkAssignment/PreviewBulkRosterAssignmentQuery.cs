using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Students.Selection;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.BulkAssignment;

/// <summary>
/// What putting this list of students into this roster would do — written by nothing.
/// </summary>
/// <remarks>
/// ⚠ It runs <c>BulkRosterAssignmentPlanner</c> and nothing else, exactly as the apply does. A
/// preview computed by different code is a preview of nothing, and this one is the whole basis of
/// the count the apply is confirmed against.
/// </remarks>
public sealed record PreviewBulkRosterAssignmentQuery(
    int TargetGroupId,
    StudentTargets Targets,
    int? AcademicYearId = null) : IQuery<BulkRosterAssignmentReport>;

internal sealed class PreviewBulkRosterAssignmentQueryValidator
    : AbstractValidator<PreviewBulkRosterAssignmentQuery>
{
    public PreviewBulkRosterAssignmentQueryValidator()
    {
        RuleFor(x => x.TargetGroupId).GreaterThan(0);
        RuleFor(x => x.Targets).NotNull();
    }
}

internal sealed class PreviewBulkRosterAssignmentQueryHandler(
    BulkRosterAssignmentPlanner planner,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<PreviewBulkRosterAssignmentQuery, BulkRosterAssignmentReport>
{
    public async Task<Result<BulkRosterAssignmentReport>> Handle(
        PreviewBulkRosterAssignmentQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BulkRosterAssignmentErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<BulkRosterAssignmentReport>(access.Error);

        // ⚠ Refused rather than answered with an empty report. « Personne n'est désigné » and
        // « personne n'est concerné » are the same zero and opposite situations: the first is a
        // request that lost its payload, the second is a list already applied.
        if (request.Targets.NamesNobody)
            return Result.Failure<BulkRosterAssignmentReport>(BulkRosterAssignmentErrors.NamesNobody);

        var plan = await planner.PlanAsync(
            request.TargetGroupId, request.AcademicYearId, request.Targets, cancellationToken);

        return plan.IsFailure
            ? Result.Failure<BulkRosterAssignmentReport>(plan.Error)
            : plan.Value.Report;
    }
}
