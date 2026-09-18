using FluentValidation;
using PGSH.Application.Extensions;

namespace PGSH.Application.AcademicGroups.Empty;

/// <summary>
/// Same split as the delete it sits beside: an omitted promotion is the year-wide scope, deliberately
/// chosen; an omitted year is nothing chosen at all.
/// </summary>
public sealed class EmptyAllYearGroupsCommandValidator : AbstractValidator<EmptyAllYearGroupsCommand>
{
    public EmptyAllYearGroupsCommandValidator()
    {
        RuleFor(x => x.AcademicYearId)
            .IsARequiredReference(
                "L'année universitaire est obligatoire : sans elle, « vider les groupes » retirerait "
                + "de leur groupe les étudiants d'une année que personne n'a nommée.");
    }
}
