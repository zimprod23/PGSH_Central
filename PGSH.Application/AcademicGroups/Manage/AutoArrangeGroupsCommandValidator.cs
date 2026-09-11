using FluentValidation;

namespace PGSH.Application.AcademicGroups.Manage;

public sealed class AutoArrangeGroupsCommandValidator : AbstractValidator<AutoArrangeGroupsCommand>
{
    public AutoArrangeGroupsCommandValidator()
    {
        RuleFor(x => x.LevelId)
            .NotEmpty()
            .WithMessage("Level is required for group distribution.");

        RuleFor(x => x.AcademicYearId)
            .NotEmpty()
            .WithMessage("Academic Year is required.");

        // ⚠ Exactly one unit. Both given, the handler would have to pick and the operator would not
        // know which; neither given, the act has no shape at all. Said here rather than defaulted,
        // because a silent default is how « 20 » ends up cutting a promotion nobody sized.
        RuleFor(x => x)
            .Must(x => (x.GroupSize is not null) ^ (x.GroupCount is not null))
            .WithMessage(
                "Indiquez soit une taille de groupe, soit un nombre de groupes — l'un des deux, pas les deux.");

        RuleFor(x => x.GroupSize!.Value)
            .GreaterThan(0)
            .WithMessage("La taille d'un groupe est d'au moins 1 étudiant.")
            .LessThanOrEqualTo(100)
            .WithMessage("Cette taille de groupe paraît trop élevée pour une rotation. Vérifiez.")
            .When(x => x.GroupSize is not null);

        // The ceiling is the promotion, not a round number: the 7ᵉ MED holds 1 347 inscriptions, so a
        // cut into a thousand rosters is arithmetically fine and almost certainly a typo — which is
        // what the handler's refusal names, with both numbers.
        RuleFor(x => x.GroupCount!.Value)
            .GreaterThan(0)
            .WithMessage("Le nombre de groupes est d'au moins 1.")
            .LessThanOrEqualTo(2000)
            .WithMessage("Ce nombre de groupes dépasse tout effectif de promotion. Vérifiez.")
            .When(x => x.GroupCount is not null);
    }
}