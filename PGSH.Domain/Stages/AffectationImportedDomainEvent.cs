using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

/// <summary>
/// One student's rotation on one stage was declared from an uploaded canevas rather than produced by
/// the planning grid.
/// </summary>
/// <param name="DroppedPeriods">
/// How many périodes the declaration destroyed. ⚠ Carried on the event because the dossier entry is
/// the only durable trace of them: once the write lands there is nothing left to count, and
/// « affectation importée » over an empty stage and over a published rotation are the same sentence
/// about two unrelated events.
/// </param>
public sealed record AffectationImportedDomainEvent(
    Guid InternshipAssignmentId,
    Guid RegistrationId,
    int StageId,
    int CreatedPeriods,
    int DroppedPeriods) : IDomainEvent;
