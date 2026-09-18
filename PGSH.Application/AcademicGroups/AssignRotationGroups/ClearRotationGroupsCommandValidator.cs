using FluentValidation;
using PGSH.Application.Extensions;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

/// <summary>
/// The undo refuses an unnamed scope for the same reason the cut does — it reaches exactly as far.
/// </summary>
public sealed class ClearRotationGroupsCommandValidator : AbstractValidator<ClearRotationGroupsCommand>
{
    public ClearRotationGroupsCommandValidator()
    {
        RuleFor(x => x.AcademicYearId)
            .IsARequiredReference(
                "L'année universitaire est obligatoire : sans elle, l'annulation du découpage "
                + "porterait sur une année que personne n'a choisie.");

        RuleFor(x => x.LevelId)
            .IsARequiredReference(
                "La promotion est obligatoire : sans elle, l'annulation retire leur étiquette de "
                + "partition à toutes les promotions de l'année, et pas seulement à celle qu'on "
                + "voulait re-découper.");
    }
}
