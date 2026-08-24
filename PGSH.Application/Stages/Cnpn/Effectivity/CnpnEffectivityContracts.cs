using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Stages.Cnpn.Effectivity;

/// <summary>
/// One authored « ce texte régit tel niveau à partir de telle année ».
/// </summary>
/// <param name="RegistrationsGoverned">
/// How many registrations already carry this text at this level from this year on. It is what makes
/// the rule's effect visible after the fact — and what a deletion has to name, since removing the
/// rule changes none of them.
/// </param>
public sealed record CnpnEffectivityResponse(
    int Id,
    int CnpnVersionId,
    string CnpnVersionCode,
    string CnpnVersionLabel,
    AcademicProgram AcademicProgram,
    int LevelId,
    string LevelLabel,
    int LevelYear,
    int FromAcademicYearId,
    string FromAcademicYearLabel,
    string? Note,
    DateTime RecordedOn,
    int RegistrationsGoverned);

/// <summary>
/// What re-stamping would do to registrations that already exist — the case where the rule was
/// authored after the réinscription had already run.
///
/// <para>Preview and apply return this same shape from the same planner, so the dry run is the plan.
/// The ordinary path needs none of it: a rule authored before the rollover is applied by
/// <c>RegistrationCnpnStamper</c> as each registration is created.</para>
/// </summary>
/// <param name="AlreadyGoverned">Registrations in scope that already name this text — nothing to do.</param>
/// <param name="WillMove">Registrations that would change text.</param>
/// <param name="FrozenByOutcome">
/// Registrations whose year has been pronounced. Refused, not forced: the verdict was recorded
/// against a requirement set, and moving that set afterwards makes the verdict unreadable. Re-open
/// the year if the rattachement is genuinely wrong.
/// </param>
/// <param name="StudentsMoved">
/// Students whose own stamp would advance with the rule — which is what changes how many years they
/// owe, so it is counted separately from the registrations.
/// </param>
public sealed record CnpnEffectivityApplyPreview(
    int EffectivityId,
    string CnpnVersionCode,
    string LevelLabel,
    string FromAcademicYearLabel,
    int InScope,
    int AlreadyGoverned,
    int WillMove,
    int FrozenByOutcome,
    int StudentsMoved,
    bool CanApply,
    IReadOnlyList<CnpnEffectivityRow> Sample,
    int SampleTotal);

public sealed record CnpnEffectivityRow(
    Guid RegistrationId,
    Guid StudentId,
    string StudentFullName,
    string? Cne,
    string AcademicYearLabel,
    string? CurrentCnpnCode,
    CnpnEffectivityRowStatus Status,
    string Message);

public enum CnpnEffectivityRowStatus
{
    WillMove,
    AlreadyGoverned,
    FrozenByOutcome,
}
