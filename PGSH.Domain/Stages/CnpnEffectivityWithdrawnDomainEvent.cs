using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// A rule was withdrawn. ⚠ <b>Prospective</b>: every registration the rule already stamped keeps its
/// text, and that is deliberate — un-stamping them would move requirement sets under students who
/// have been studying against them. What changes is which text the <i>next</i> registration at that
/// level resolves to, which is why the removal is worth announcing at all.
/// </summary>
public sealed record CnpnEffectivityWithdrawnDomainEvent(
    int CnpnVersionId,
    string Code,
    int LevelId,
    int FromAcademicYearId) : IDomainEvent;
