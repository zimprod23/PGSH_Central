using FluentValidation;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Targeting;

/// <summary>
/// Freezes the previewed population onto the text.
///
/// <para>The rule itself is not stored as an entity: it is applied once, and what survives is the
/// membership (<c>Student.CnpnVersionId</c>) plus this command's audit entry recording the criteria,
/// the author and the date. Keeping the rule as live state would be worse than useless — re-evaluated
/// next September, "année ≤ 2" selects a different set of people, and the whole point of the stamp is
/// that a student's text does not move under them.</para>
/// </summary>
public sealed record ApplyCnpnTargetCommand(int CnpnVersionId, CnpnTargetCriteria Criteria)
    : ICommand<CnpnTargetPreview>, IAuditableCommand
{
    public string  AuditAction     => "CNPN_TARGET_APPLIED";
    public string  AuditEntityType => "CnpnVersion";
    public string? AuditEntityId   => CnpnVersionId.ToString();

    public string? AuditMetadata =>
        $$"""
          {"program":"{{Criteria.Program}}","maxLevelYear":{{Criteria.MaxLevelYear}},
           "asOfAcademicYearId":{{Criteria.AsOfAcademicYearId?.ToString() ?? "null"}},
           "includeEntryContradictions":{{(Criteria.IncludeEntryContradictions ? "true" : "false")}}}
          """.ReplaceLineEndings(string.Empty);
}

internal sealed class ApplyCnpnTargetCommandValidator : AbstractValidator<ApplyCnpnTargetCommand>
{
    public ApplyCnpnTargetCommandValidator()
    {
        RuleFor(x => x.CnpnVersionId).GreaterThan(0);
        RuleFor(x => x.Criteria.Program).IsInEnum();

        // 1 is the first study year; 0 is "Retrait" and is never a target.
        RuleFor(x => x.Criteria.MaxLevelYear)
            .InclusiveBetween(1, 10)
            .WithMessage("L'année visée doit être comprise entre 1 et 10.");
    }
}

internal sealed class ApplyCnpnTargetCommandHandler(
    IApplicationDbContext dbContext,
    CnpnTargetPlanner planner,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyCnpnTargetCommand, CnpnTargetPreview>
{
    public async Task<Result<CnpnTargetPreview>> Handle(
        ApplyCnpnTargetCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure)
            return Result.Failure<CnpnTargetPreview>(access.Error);

        var plan = await planner.PlanAsync(request.CnpnVersionId, request.Criteria, cancellationToken);
        if (plan.IsFailure)
            return Result.Failure<CnpnTargetPreview>(plan.Error);

        if (!plan.Value.Preview.CanApply)
            return Result.Failure<CnpnTargetPreview>(CnpnErrors.TargetNothingToApply);

        foreach (var item in plan.Value.Work)
        {
            // isInferred: false — an applied rule is a decision, not a deduction, even for the rows
            // the arrêté's own wording argues against: the faculty saw those and said yes.
            var result = item.Student.AssignCnpnVersion(request.CnpnVersionId, isInferred: false);

            // The planner cleared every guard this can return, so a failure here means the plan and
            // the aggregate disagree — refuse the batch rather than write part of it.
            if (result.IsFailure)
                return Result.Failure<CnpnTargetPreview>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return plan.Value.Preview;
    }
}
