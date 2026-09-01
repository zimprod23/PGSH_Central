using PGSH.SharedKernel;

namespace PGSH.Domain.Students;

/// <summary>
/// A student's CNPN stamp was removed because no text of his programme could be resolved for him —
/// today, only a réorientation into a programme PGSH holds no applicable text for.
///
/// <para>Its own event rather than an assignment to null, because it is its own fact. Null on
/// <c>Student.CnpnVersionId</c> means « never resolved », never « owes nothing », and every reader
/// already falls back on it gracefully — so removing a stamp that names a cursus the student has left
/// states less than before, and states it truthfully. Keeping it would make <c>TotalYears</c>, and
/// therefore how many years he owes, answer from the wrong arrêté.</para>
/// </summary>
public sealed record StudentCnpnVersionClearedDomainEvent(
    Guid StudentId,
    int PreviousCnpnVersionId) : IDomainEvent;
