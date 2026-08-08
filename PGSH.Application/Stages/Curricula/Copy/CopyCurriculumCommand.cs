using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Curricula.Copy;

/// <summary>
/// Seeds one CNPN's requirements for a level from another CNPN's. A new text mostly repeats the
/// previous one with an edit or two, so cloning then amending is the realistic flow — and it keeps
/// each version an independent record rather than a diff nobody can read back.
/// </summary>
public sealed record CopyCurriculumCommand(int LevelId, int FromCnpnVersionId, int ToCnpnVersionId)
    : ICommand<int>, IAuditableCommand
{
    public string  AuditAction     => "CURRICULUM_COPIED";
    public string  AuditEntityType => "Curriculum";
    public string? AuditEntityId   => $"{ToCnpnVersionId}/{LevelId}";
    public string? AuditMetadata   => $"{{\"fromCnpnVersionId\":{FromCnpnVersionId}}}";
}
