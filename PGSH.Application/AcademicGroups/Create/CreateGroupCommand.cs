using FluentValidation;
using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Create;

public sealed record CreateGroupCommand(
    string  Label,
    int     AcademicYearId,
    int?    LevelId,
    string? GeographicZone,
    string? RotationGroup) : ICommand<int>;

internal sealed class CreateGroupCommandValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AcademicYearId).GreaterThan(0);
        RuleFor(x => x.GeographicZone).MaximumLength(200).When(x => x.GeographicZone is not null);
        RuleFor(x => x.RotationGroup).MaximumLength(10).When(x => x.RotationGroup is not null);
    }
}
