using FluentValidation;
using PGSH.Application.Extensions;

namespace PGSH.Application.AcademicGroups.DeleteAll;

/// <summary>
/// ⚠ <b>The promotion is optional here and the year is not</b>, and the asymmetry is the point: an
/// omitted <c>LevelId</c> is a scope the caller chose — the year-wide act — while an omitted year is
/// no scope at all. Resolving it to « l'année en cours » would let a blank selector destroy the
/// rosters and cohortes of the promotion everybody is currently working on.
/// </summary>
public sealed class DeleteAllGroupsCommandValidator : AbstractValidator<DeleteAllGroupsCommand>
{
    public DeleteAllGroupsCommandValidator()
    {
        RuleFor(x => x.AcademicYearId)
            .IsARequiredReference(
                "L'année universitaire est obligatoire : cette suppression détruit des groupes et "
                + "les cohortes qui en dépendent, et elle ne peut pas porter sur une année devinée.");
    }
}
