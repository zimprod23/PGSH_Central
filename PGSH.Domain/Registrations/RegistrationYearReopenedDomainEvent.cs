using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// A verdict was withdrawn and the academic year put back in progress. Distinct from
/// <see cref="RegistrationYearOutcomeRecordedDomainEvent"/> because it is not a decision about the
/// student — it is the record of one being taken back, which is exactly what a reader of the timeline
/// must be able to tell apart from a jury that changed its mind.
/// </summary>
public sealed record RegistrationYearReopenedDomainEvent(
    Guid RegistrationId,
    Guid StudentId,
    int AcademicYearId,
    RegistrationStatus WithdrawnOutcome,
    string? Reason) : IDomainEvent;
