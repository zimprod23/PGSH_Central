using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Curricula.Save;

/// <summary>
/// Records what one CNPN requires of one level — creating the set, or replacing what was there.
///
/// <para>
/// The whole set is submitted at once rather than stage-by-stage because that is how a CNPN is
/// issued and amended: as one text. Reconciling against the stored set also means removals go through
/// <c>Curriculum.RemoveStage</c> and raise their domain event, which stage-by-stage endpoints would
/// only do by accident.
/// </para>
/// </summary>
public sealed record SaveCurriculumCommand(
    int LevelId,
    int CnpnVersionId,
    string? Reference,
    IReadOnlyList<CurriculumStageInput> Stages)
    : ICommand<int>, IAuditableCommand
{
    public string  AuditAction     => "CURRICULUM_SAVED";
    public string  AuditEntityType => "Curriculum";
    public string? AuditEntityId   => $"{CnpnVersionId}/{LevelId}";
    public string? AuditMetadata   => $"{{\"stageCount\":{Stages.Count}}}";
}

public sealed record CurriculumStageInput(int StageId, int Coefficient, int DurationInDays);
