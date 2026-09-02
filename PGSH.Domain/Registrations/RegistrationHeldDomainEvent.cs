using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// A registration was withdrawn from planning pending a human decision.
/// </summary>
/// <remarks>
/// Raised because the act is wide and otherwise unobservable: it decides that a student takes no
/// part in the year's répartition. The déliberation's verdict and the CNPN stamp both raise one, and
/// this changes as much about a student's year as either.
/// </remarks>
public sealed record RegistrationHeldDomainEvent(
    Guid RegistrationId,
    Guid StudentId,
    int AcademicYearId,
    RegistrationHoldReason Reason,
    string Evidence) : IDomainEvent;
