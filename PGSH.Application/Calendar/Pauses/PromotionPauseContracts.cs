using PGSH.Domain.Stages;

namespace PGSH.Application.Calendar.Pauses;

/// <summary>
/// One declared window, as a screen shows it.
/// </summary>
/// <param name="WorkingDaysLost">
/// What the window actually costs: worked days inside it, counted on the <b>faculty</b> calendar. A
/// window laid over a weekend or over Aïd costs less than its length, and saying so stops it being
/// blamed for a rotation that did not move. ⚠ Never counted on the promotion's own calendar, which
/// already contains this window and would therefore report every one of them as costing zero.
/// </param>
public sealed record PromotionPauseResponse(
    int Id,
    int AcademicYearId,
    string AcademicYearLabel,
    int LevelId,
    string LevelLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    int DayCount,
    int WorkingDaysLost,
    PauseKind Kind,
    string Reason,
    bool IsConfirmed,
    DateTime RecordedOn);

/// <summary>
/// What a window touches, counted <b>before</b> any write — the numbers a confirmation names.
/// </summary>
/// <param name="PeriodsUnderway">
/// Rotations open across the window: students standing in a service that week. Reported apart from the
/// total because it is the number that decides whether the axis is worth re-laying — a
/// <c>Planned</c> rotation can still be republished, an open one is happening.
/// </param>
public sealed record PromotionPauseSpan(int SlotsSpanning, int PeriodsSpanning, int PeriodsUnderway);

/// <summary>
/// What declaring this window would cost the promotion, measured against the plan already laid.
/// Writes nothing.
/// </summary>
/// <remarks>
/// ⚠ <b>Every list here is bounded by construction and the counts are measured before any cap.</b> A
/// promotion holds one créneau per stage per column and roughly a hundred cohortes, so a single object
/// carrying « one row per cohorte » is the shape that shipped 4 725 students in one response. The rows
/// are créneaux and stages; cohortes, périodes and students are counted, never listed.
/// </remarks>
/// <param name="WorkingDaysLost">
/// Worked days the window removes from this promotion's calendar — its length minus the weekends and
/// the faculty holidays already inside it.
/// </param>
/// <param name="CalendarIsEmpty">
/// No faculty holiday is recorded anywhere across the window, so « jours ouvrables » here means
/// "calendar days minus weekends" — narrower than it says, and the normal state of a fresh base.
/// </param>
/// <param name="SlotsTruncated">
/// True when more créneaux cross the window than <see cref="PromotionPauseImpactReader.MaxSlotRows"/>
/// lists. <paramref name="SlotsSpanning"/> is still the true count.
/// </param>
/// <param name="PublishedCellsInGrid">
/// Cells of the promotion's whole grid that a période was published from. ⚠ <b>Non-zero means the axis
/// cannot be re-laid</b> — <c>ApplyRotationCycleCommand</c> refuses on it — so the shortfall this report
/// counts has no remedy today beyond accepting it. Measured on the live base 2026-09-06: the 3ᵉ MED
/// holds 804. Moving a published column is <c>PHASES.md</c> §17.1 and is not built.
/// </param>
/// <param name="Warnings">
/// Computed from what was actually found, never emitted unconditionally: a caption that fires whatever
/// the data says is noise, and noise is dismissed.
/// </param>
public sealed record PromotionPauseImpactResponse(
    int AcademicYearId,
    string AcademicYearLabel,
    int LevelId,
    string LevelLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    int CalendarDays,
    int WorkingDaysLost,
    bool CalendarIsEmpty,
    int SlotsSpanning,
    int CellsSpanning,
    int CohortsSpanning,
    int PeriodsSpanning,
    int PeriodsPlanned,
    int PeriodsUnderway,
    int PeriodsClosed,
    int StudentsAffected,
    IReadOnlyList<PromotionPauseStageImpact> Stages,
    IReadOnlyList<PromotionPauseSlotImpact> Slots,
    bool SlotsTruncated,
    int PublishedCellsInGrid,
    IReadOnlyList<string> Warnings);

/// <summary>
/// One stage of the promotion, and what the window takes out of the columns it holds across it.
/// </summary>
/// <param name="StatedDurationInDays">
/// ⚠ <c>Stage.DurationInDays</c>, the catalogue's own number — which is already in worked days for 25
/// of the 27 stages. Reported beside the measured figures rather than compared to them: which column
/// is authoritative is still open (<c>PHASES.md</c> 15.1), so this is a report and not a guard.
/// </param>
public sealed record PromotionPauseStageImpact(
    int StageId,
    string Name,
    int StatedDurationInDays,
    int SlotsSpanning,
    int WorkingDaysLost,
    int MinWorkingDaysAfter,
    int MaxWorkingDaysAfter);

/// <param name="WorkingDaysBefore">The column's worked days without the window.</param>
/// <param name="WorkingDaysAfter">
/// And with it. The difference is what this column loses if the axis is not re-laid — the créneau keeps
/// the dates it was given, so the days come out of the stage rather than off the end.
/// </param>
public sealed record PromotionPauseSlotImpact(
    int StageSlotId,
    int StageId,
    string StageName,
    int PeriodNumber,
    string? Label,
    DateOnly StartDate,
    DateOnly EndDate,
    int WorkingDaysBefore,
    int WorkingDaysAfter,
    int CellsSpanning);

/// <summary>What declaring wrote, and what it costs the plan already laid.</summary>
public sealed record PromotionPauseDeclaredResult(
    int Id,
    DateOnly StartDate,
    DateOnly EndDate,
    int WorkingDaysLost,
    int SlotsSpanning,
    int PeriodsSpanning,
    int PeriodsUnderway);

/// <summary>
/// What the correction cost, in the same terms as <see cref="PromotionPauseRevokedResult"/> — moving a
/// window off a date is the same event as removing it from there.
/// </summary>
/// <param name="DatesMoved">
/// False when only the reason, the kind or the confirmed flag changed. Ticking « dates confirmées » on
/// a window already right costs nothing, and reporting créneaux there would train the reader to dismiss
/// the one report that matters. Same gate as <c>UpdateHolidayResult.DatesMoved</c>.
/// </param>
/// <param name="SlotsSpanning">
/// Créneaux crossing the span the window <b>left</b> or the span it <b>arrived at</b>, counted once and
/// counted <b>before</b> the write. Both halves are affected and for opposite reasons: the first was
/// measured against a window that is no longer there, the second now contains one it never counted.
/// Zero when <paramref name="DatesMoved"/> is false.
/// </param>
public sealed record PromotionPauseCorrectedResult(
    int Id,
    string Reason,
    DateOnly StartDate,
    DateOnly EndDate,
    bool DatesMoved,
    int SlotsSpanning,
    int PeriodsSpanning,
    int PeriodsUnderway);

/// <summary>
/// What the revocation gave back.
/// </summary>
/// <param name="HadBegun">
/// The window had already started when it was revoked. ⚠ The removal is <b>prospective either way</b>:
/// nothing was pushed when it was declared, so nothing is walked back — créneaux keep the dates they
/// were given and périodes keep what actually happened. What changes is every measurement from now on.
/// </param>
public sealed record PromotionPauseRevokedResult(
    string Reason,
    DateOnly StartDate,
    DateOnly EndDate,
    bool HadBegun,
    int SlotsSpanning,
    int PeriodsSpanning,
    int PeriodsUnderway);
