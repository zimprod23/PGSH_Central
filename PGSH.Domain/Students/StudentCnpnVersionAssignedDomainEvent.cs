using PGSH.SharedKernel;

namespace PGSH.Domain.Students;

/// <summary>
/// A student was placed under a CNPN, or moved between two. Announced because it is not a bookkeeping
/// detail: the text decides how many years the student owes and which stages count, so a correction
/// applied late can change whether someone is due to graduate. <paramref name="IsInferred"/> marks an
/// assignment deduced from the level the student sits in rather than read from a recorded entry.
/// </summary>
public sealed record StudentCnpnVersionAssignedDomainEvent(
    Guid StudentId,
    int? PreviousCnpnVersionId,
    int NewCnpnVersionId,
    bool IsInferred) : IDomainEvent;
