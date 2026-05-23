using FluentValidation;

namespace PGSH.Application.Stages.Cohorts.RotationPlan;

internal sealed class CreateRotationPlanCommandValidator : AbstractValidator<CreateRotationPlanCommand>
{
    public CreateRotationPlanCommandValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);

        RuleFor(x => x.CohortIds)
            .NotEmpty().WithMessage("At least one cohort must be selected.");

        RuleFor(x => x.Slots)
            .NotEmpty().WithMessage("At least one rotation slot must be defined.");

        RuleForEach(x => x.Slots).ChildRules(slot =>
        {
            slot.RuleFor(s => s.ServiceId).GreaterThan(0);
            slot.RuleFor(s => s.StartDate).NotEmpty();
            slot.RuleFor(s => s.EndDate)
                .GreaterThanOrEqualTo(s => s.StartDate)
                .WithMessage("End date must be after start date.");
        });
    }
}
