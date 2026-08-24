using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// « L'année en cours » moved. Worth an event rather than a silent flag flip: every handler that omits
/// a year resolves to this row, so the change reaches every screen at once and nothing else on the
/// record says when it happened or who did it.
/// </summary>
public sealed record AcademicYearBecameCurrentDomainEvent(
    int AcademicYearId,
    string Label) : IDomainEvent;
