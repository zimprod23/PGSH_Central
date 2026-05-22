using FluentValidation;

namespace PGSH.Application.Stages.Evaluations.Create;

public sealed class CreateServiceEvaluationCommandValidator : AbstractValidator<CreateServiceEvaluationCommand>
{
    public CreateServiceEvaluationCommandValidator()
    {
        RuleFor(x => x.ServicePeriodId).NotEmpty();
        RuleFor(x => x.TotalScore).InclusiveBetween(0, 20);
        RuleFor(x => x.ObjectiveScores).NotNull();
        RuleForEach(x => x.ObjectiveScores).ChildRules(o =>
        {
            o.RuleFor(s => s.StageObjectiveId).GreaterThan(0);
            o.RuleFor(s => s.Score).GreaterThanOrEqualTo(0);
        });
    }
}
