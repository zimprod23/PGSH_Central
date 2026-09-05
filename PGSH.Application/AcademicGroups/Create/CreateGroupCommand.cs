using FluentValidation;
using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Create;

public sealed record CreateGroupCommand(
    string  Label,
    int     AcademicYearId,
    int?    LevelId,
    string? GeographicZone,
    string? RotationGroup) : ICommand<int>, IAuditableCommand
{
    public string AuditAction => "GROUP_CREATED";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();

    // ⚠ Through AuditMetadataJson rather than interpolated: Label is free text somebody typed, so a
    // quote or a backslash in it would write metadata that is not JSON — in the one column whose
    // whole purpose is to be read back later.
    public string? AuditMetadata => AuditMetadataJson.Of(
        ("label", Label),
        ("levelId", LevelId),
        ("rotationGroup", RotationGroup));
}

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
