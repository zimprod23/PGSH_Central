using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// A stage was dropped from a level's CNPN. Announced because it does not settle anything on its own:
/// students who failed that stage under an earlier text still owe it, and the administration has to
/// decide case by case whether an equivalent is served or the obligation is lifted.
/// </summary>
public sealed record CurriculumStageRemovedDomainEvent(
    int CurriculumId,
    int LevelId,
    int CnpnVersionId,
    int StageId) : IDomainEvent;
