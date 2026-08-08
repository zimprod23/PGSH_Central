using FluentValidation;

namespace PGSH.Application.Stages.Curricula.Save;

public sealed class SaveCurriculumCommandValidator : AbstractValidator<SaveCurriculumCommand>
{
    public SaveCurriculumCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.CnpnVersionId).GreaterThan(0);
        RuleFor(x => x.Reference).MaximumLength(200);

        RuleFor(x => x.Stages).NotNull();

        RuleForEach(x => x.Stages).ChildRules(stage =>
        {
            stage.RuleFor(s => s.StageId).GreaterThan(0);
            stage.RuleFor(s => s.Coefficient).GreaterThanOrEqualTo(1);
            stage.RuleFor(s => s.DurationInDays).GreaterThan(0);
        });

        // A text cannot require the same stage twice; caught here so the aggregate is never handed
        // a set it would have to reject halfway through applying.
        RuleFor(x => x.Stages)
            .Must(s => s.Select(x => x.StageId).Distinct().Count() == s.Count)
            .WithMessage("Un même stage ne peut figurer qu'une fois dans un CNPN.");
    }
}
