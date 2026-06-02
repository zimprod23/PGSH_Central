using FluentValidation;

namespace PGSH.Application.Stages.MacroPlan;

internal sealed class GenerateMacroPlanCommandValidator : AbstractValidator<GenerateMacroPlanCommand>
{
    public GenerateMacroPlanCommandValidator()
    {
        RuleFor(x => x.AcademicYearId).GreaterThan(0);
        RuleFor(x => x.Plans).NotEmpty().WithMessage("At least one partition-stage plan is required.");

        RuleForEach(x => x.Plans).ChildRules(plan =>
        {
            plan.RuleFor(p => p.RotationGroup).NotEmpty();
            plan.RuleFor(p => p.StageId).GreaterThan(0);
        });
    }
}
