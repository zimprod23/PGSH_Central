using FluentValidation;

namespace PGSH.Application.Stages.Evaluations.Update;

public sealed class UpdateServiceEvaluationCommandValidator : AbstractValidator<UpdateServiceEvaluationCommand>
{
    public UpdateServiceEvaluationCommandValidator()
    {
        RuleFor(x => x.EvaluationId).NotEmpty();
        RuleFor(x => x.TotalScore).InclusiveBetween(0, 20);
        RuleFor(x => x.ObjectiveScores).NotNull();
        RuleForEach(x => x.ObjectiveScores).ChildRules(o =>
        {
            o.RuleFor(s => s.StageObjectiveId).GreaterThan(0);
            o.RuleFor(s => s.Score).GreaterThanOrEqualTo(0);
        });
    }
}
