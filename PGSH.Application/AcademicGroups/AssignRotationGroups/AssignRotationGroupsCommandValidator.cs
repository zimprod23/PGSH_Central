using FluentValidation;
using PGSH.Application.Extensions;

namespace PGSH.Application.AcademicGroups.AssignRotationGroups;

/// <summary>
/// ⚠ <b>A cut names its own scope; it never infers one.</b> An omitted academic year means « the
/// current one » on a <i>read</i> — that is <c>AcademicYearResolver</c>'s rule and it is right there.
/// It is the wrong rule here: this act writes a partition label onto every roster it reaches, and a
/// year nobody named is a year nobody consented to. The same goes for the promotion, which is the
/// guard rather than a filter (see <see cref="AssignRotationGroupsCommand"/>).
/// </summary>
public sealed class AssignRotationGroupsCommandValidator : AbstractValidator<AssignRotationGroupsCommand>
{
    public AssignRotationGroupsCommandValidator()
    {
        RuleFor(x => x.AcademicYearId)
            .IsARequiredReference(
                "L'année universitaire est obligatoire : un découpage en groupes de rotation "
                + "appartient à une promotion d'une année précise, et un découpage porté sur une "
                + "année devinée réécrirait les groupes de celle qui est en cours.");

        RuleFor(x => x.LevelId)
            .IsARequiredReference(
                "La promotion à découper est obligatoire : sans elle le découpage atteint toutes les "
                + "promotions de l'année d'un coup — dont leurs comptes de partitions diffèrent — "
                + "ainsi que « Non réparti », qui n'appartient à aucune promotion et ne doit jamais "
                + "porter d'étiquette.");
    }
}
