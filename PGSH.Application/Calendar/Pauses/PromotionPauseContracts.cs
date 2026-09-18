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
/// <param name="SlotsSpanning">
/// Columns of this promotion's axis that the window cuts.
///
/// <para>⚠ <b>Zero is the <i>good</i> answer, not a missing one</b>, and the screen must say which:
/// a window declared <b>before</b> the axis is laid crosses nothing, because the axis then steps over
/// it — that is the whole mechanism working. Zero also arises on a promotion with no axis yet. What a
/// non-zero count means is the other thing entirely: a plan already laid is being cut, and declaring
/// moves none of it. Rendering the two alike is how « déclarée à temps » and « déclarée trop tard »
/// become the same row.</para>
/// </param>
/// <param name="PeriodsSpanning">
/// Rotations of this promotion open across the window — students whose service dates contain days
/// nobody is going to serve, and whose end dates do not account for it. ⚠ Counted through the
/// <b>registration</b>, so a student re-taking an earlier year's stage is measured against his own
/// promotion's exams.
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
    int SlotsSpanning,
    int PeriodsSpanning,
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
/// cannot be re-laid</b> — <c>ApplyRotationCycleCommand</c> refuses on it, for the <em>whole</em> year
/// and not merely for the columns this window crosses. Measured on the live base: the 3ᵉ MED holds
/// 1 000. It is <b>not</b> a statement that the shortfall is unrepairable — see
/// <paramref name="SlotsMovable"/>.
/// </param>
/// <param name="SlotsMovable">
/// How many of <paramref name="SlotsSpanning"/> the column move (<c>PHASES.md</c> §17.1) would accept:
/// those carrying no rotation that has begun, been marked or been pointed. A crossed column with no
/// published période counts as movable — there is nothing for the act to refuse.
///
/// <para>⚠ <b>This is the half the report used to omit, and omitting it read as « rien à faire ».</b>
/// « Reposer est refusé » is true and, alone, useless: what repairs a published promotion is moving the
/// crossed columns one at a time. Counted against <c>ServicePeriodLifecycle.Movable</c> — the rule
/// <c>InternshipAssignment.Reschedule</c> itself refuses on — so this number cannot promise a move the
/// act then declines.</para>
///
/// <para>⚠ <b>Movable is not the same as repaired.</b> A move shifts one column and leaves the ones
/// after it where they are; nothing cascades. The warning says so, because an operator who moves P7 and
/// expects P8 to follow has been told half a truth.</para>
/// </param>
/// <param name="SlotsEmptied">
/// How many of the crossed columns the window leaves with <b>no worked day at all</b> — a subset of
/// <paramref name="SlotsSpanning"/>.
///
/// <para>⚠ <b>Emptied is not shortened, and one number could not hold both.</b> A column keeping 12 of
/// its 15 days needs a shift; a column keeping <b>0</b> is a rotation during which its students serve
/// nothing, while its cells and its périodes still stand — the column is a hole, not a short week.
/// Measured on the live 3ᵉ MED 17/09/2026: a window over December 2026 (23 worked days against columns
/// of 15) empties <b>one column of every one of the 8 stages</b>, and the only trace of it was the left
/// end of a « 0 – 10 » range in a table cell.</para>
/// </param>
/// <param name="CellsInEmptiedSlots">
/// The cells sitting in those columns — i.e. how many cohortes would serve a rotation with no worked
/// day. ⚠ Reported beside the count because « a column nobody is in is emptied » and « a column holding
/// a hundred students is emptied » are the two states that number has to separate.
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
    int SlotsMovable,
    int SlotsEmptied,
    int CellsInEmptiedSlots,
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


/// <summary>
/// What a « Démarrer » over a selection is about to walk into: the rotations it would start, and how
/// many of them run through a window the promotion has declared.
/// </summary>
/// <param name="PeriodsToStart">
/// The denominator, and it is not optional. « 296 rotations traversent une fenêtre » says nothing
/// without « sur combien » — 296 of 296 is a column to re-lay, 296 of 4 000 is a detail.
/// </param>
/// <param name="WindowsDeclaredForPromotion">
/// Every window declared this year for the promotions present in the selection, <b>including those
/// this selection does not cross</b>.
///
/// <para>⚠ <b>This is the field that stops silence being read as safety, and it is the whole reason
/// the response is not just a count.</b> <paramref name="PeriodsCrossing"/> = 0 has two opposite
/// meanings: the promotion has declared its exam weeks and this selection genuinely misses them —
/// clear to proceed — or <b>nobody has declared anything at all</b>, in which case the zero measures
/// the absence of a faculty document, not the absence of a clash. On 18/09/2026 the whole of
/// 2026-2027 held a single declared window, so the second reading is the ordinary one and the screen
/// has to be able to tell them apart.</para>
/// </param>
/// <param name="Windows">
/// Only the windows actually crossed, each with what it costs and how many of the rotations it takes.
/// ⚠ A rotation spanning two windows appears in both rows and <b>once</b> in
/// <paramref name="PeriodsCrossing"/>: the rows are per window, the total is per rotation, and adding
/// the rows would over-count exactly the students who are worst affected.
/// </param>
public sealed record StagePauseCrossingsResponse(
    int StageId,
    string StageName,
    int AcademicYearId,
    string AcademicYearLabel,
    int PeriodsToStart,
    int PeriodsCrossing,
    int WindowsDeclaredForPromotion,
    IReadOnlyList<CrossedWindowResponse> Windows);

/// <summary>
/// One declared window that the selection runs into.
/// </summary>
/// <param name="WorkingDaysLost">
/// Worked days the window removes, on the <b>faculty</b> calendar — the same figure the pause list
/// shows, so the two screens cannot price one window differently.
/// </param>
/// <param name="PeriodsCrossing">
/// Rotations of this selection open across this window. ⚠ These are days inside a stay that nobody
/// will serve, and the stay's end date does <b>not</b> move to replace them: declaring writes no
/// dates. What repairs it is moving the columns, one at a time.
/// </param>
public sealed record CrossedWindowResponse(
    int PauseId,
    int LevelId,
    string LevelLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    PauseKind Kind,
    string Reason,
    bool IsConfirmed,
    int WorkingDaysLost,
    int PeriodsCrossing);

