using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// An academic year was closed with a verdict. Carries <paramref name="Source"/> so the timeline can
/// say whether the faculty declared it or PGSH deduced it — a distinction the reader of a student's
/// file needs and cannot recover from the status alone.
/// </summary>
public sealed record RegistrationYearOutcomeRecordedDomainEvent(
    Guid RegistrationId,
    Guid StudentId,
    int AcademicYearId,
    RegistrationStatus PreviousStatus,
    RegistrationStatus Outcome,
    RegistrationOutcomeSource Source) : IDomainEvent;
