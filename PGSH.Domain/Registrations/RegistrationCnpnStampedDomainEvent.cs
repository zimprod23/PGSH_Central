using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// Raised when a registration is given the CNPN that governs it, or when an authored effectivity
/// rule moves one that had already been stamped. <paramref name="PreviousCnpnVersionId"/> is null on
/// the first stamp and non-null on a re-stamp, which is the case worth an audit trail: it is the
/// only way a student's requirement set changes for a year already created.
/// </summary>
public sealed record RegistrationCnpnStampedDomainEvent(
    Guid RegistrationId,
    Guid StudentId,
    int AcademicYearId,
    int LevelId,
    int? PreviousCnpnVersionId,
    int CnpnVersionId,
    RegistrationCnpnSource Source) : IDomainEvent;
