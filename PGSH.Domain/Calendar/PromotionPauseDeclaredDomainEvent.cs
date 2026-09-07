using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Domain.Calendar;

/// <summary>
/// A promotion declared a window during which it is not in its services.
///
/// <para>Announced because it is the widest act in the planning area: it changes what <em>every</em>
/// stage of a promotion is measured against, and it does so without touching a single row of the grid
/// — so nothing else in the system observes it happening. Same reason
/// <c>CnpnEffectivityDeclaredDomainEvent</c> exists: a rule that will decide dates from now on used to
/// be written in silence.</para>
/// </summary>
/// <remarks>
/// ⚠ There is deliberately no revoked twin. A revocation removes the aggregate root, and EF detaches a
/// deleted entity before <c>ApplicationDbContext</c> collects domain events from the change tracker —
/// an event raised there would be dropped without a trace, which is worse than not raising one. The
/// register records it instead, under <c>PROMOTION_PAUSE_REVOKED</c>.
/// </remarks>
public sealed record PromotionPauseDeclaredDomainEvent(
    int AcademicYearId,
    string AcademicYearLabel,
    int LevelId,
    string LevelLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    PauseKind Kind,
    string Reason) : IDomainEvent;

/// <summary>
/// A declared window was corrected — most often the day its estimated dates were settled.
/// </summary>
/// <param name="DatesMoved">
/// False when only the reason, the kind or the confirmed flag changed. The window then costs exactly
/// the same worked days it did before, and a subscriber that reprints anything should not.
/// </param>
public sealed record PromotionPauseCorrectedDomainEvent(
    int PromotionPauseId,
    int AcademicYearId,
    int LevelId,
    DateOnly PreviousStartDate,
    DateOnly PreviousEndDate,
    DateOnly StartDate,
    DateOnly EndDate,
    bool DatesMoved) : IDomainEvent;
