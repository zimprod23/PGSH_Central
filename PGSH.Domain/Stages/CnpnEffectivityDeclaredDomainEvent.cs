using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// A text was declared to govern a level from an academic year onward.
///
/// <para>Announced because it is the widest act in this area and the only one nothing else can
/// observe: stamping a registration raises <c>RegistrationCnpnStampedDomainEvent</c> and moving a
/// student raises <c>StudentCnpnVersionAssignedDomainEvent</c>, but the rule that will decide the
/// text of every registration created at that level from that year on used to be written in
/// silence. Nothing already stamped moves — the rule is read once, as each registration is created —
/// so this says what will happen, not what just did.</para>
/// </summary>
public sealed record CnpnEffectivityDeclaredDomainEvent(
    int CnpnVersionId,
    string Code,
    int LevelId,
    string LevelLabel,
    int FromAcademicYearId,
    string FromAcademicYearLabel) : IDomainEvent;
