using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Domain.Calendar;

/// <summary>
/// A declared window during which one promotion — <b>(année universitaire, niveau)</b> — is not in
/// its services: an exam session, most often.
///
/// <para><b>Why the unit is the promotion.</b> Two promotions rotate through the same services on the
/// same morning and only one of them is sitting an exam. The act is therefore never faculty-wide (a
/// <see cref="Holiday"/> is that) and never per stage: a promotion sits its exams across every stage
/// it is running at once, and <c>Stage.LevelId</c> only happens to name a promotion today — a stage is
/// planned to span two levels.</para>
/// </summary>
/// <remarks>
/// <para>⚠ <b>This is a calendar fact, not a second date-pushing mechanism, and declaring one moves no
/// date by itself.</b> The window joins that promotion's working-day calendar
/// (<c>WorkingDayProvider.ForPromotionAsync</c>), and every reader that measures or lays anything in
/// <em>jours ouvrables</em> then compensates on its own: the axis generator lays columns that step over
/// the window, the rotation-cycle preview counts a stage's worked days without it, the export reports
/// the days actually served. Because dates are <b>derived from</b> the calendar rather than added to
/// what is already stored, declaring the same window twice produces the same dates — the property that
/// makes the act correctable and revocable at all. <c>InternshipAssignment.ResumePeriod</c> is the
/// opposite: it <i>accumulates</i>, so running it twice moves the rotation twice.</para>
///
/// <para>⚠ <b>What it therefore does to a grid already laid: nothing, deliberately.</b> A window
/// declared after the axis was authored leaves the créneaux and the périodes exactly where they are, and
/// they are now short by the worked days it takes. That shortfall is what the preview counts, per stage
/// and per créneau. Silently rewriting the dates of a published promotion is the one thing this must
/// not do.</para>
///
/// <para>⚠ <b>And whether that shortfall can be repaired depends on something outside this aggregate —
/// measured on the live base 2026-09-06, not reasoned about.</b> Re-laying the axis is a real act
/// (<c>ApplyRotationCycleCommand</c>) <em>only while nothing has been published from it</em>: that
/// command refuses on <c>PublishedCells &gt; 0</c>, and the 3ᵉ MED holds <b>804</b>. So for a promotion
/// whose grid is published — precisely the one a late window hurts — there is <b>no remedy today</b>:
/// the window is recorded, the report is honest, and the days are simply lost until moving a published
/// column exists (<c>PHASES.md</c> §17.1). The preview says which of the two situations it is in rather
/// than prescribing a button that would refuse.</para>
///
/// <para>⚠ <b>Revoking is prospective, and the same reasoning makes that free.</b> Nothing was pushed,
/// so nothing has to be walked back: removing the window puts the days back into the promotion's
/// calendar and every later measurement changes accordingly, exactly as deleting a
/// <see cref="Holiday"/> does. Créneaux already laid around it keep the dates they were given — the
/// revocation reports how many — and périodes already served keep what actually happened. Same rule as
/// <c>CnpnVersion.WithdrawEffectivity</c>: a removal decides what happens <i>next</i>.</para>
///
/// <para>The properties carry <c>init</c> accessors over explicit backing fields, as
/// <see cref="CnpnVersion"/> does and for the same reason: an object initialiser — how the seeder, a
/// migration and the tests build one — still works, while nothing changes a window afterwards except
/// through <see cref="Correct"/>.</para>
/// </remarks>
public sealed class PromotionPause : Entity, ICalendarClosure
{
    /// <summary>
    /// A ceiling, not a statement about any window. An exam session is a week or two, and the longest
    /// thing plausibly declared here is a month; beyond a term the right row is a faculty
    /// <see cref="Holiday"/> of kind <see cref="HolidayKind.Academic"/>, which is not promotion-scoped
    /// and does not quietly make one promotion's stages unmeasurable.
    /// </summary>
    public const int MaxSpanDays = 120;

    private DateOnly _startDate;
    private DateOnly _endDate;
    private PauseKind _kind;
    private string _reason = default!;
    private bool _isConfirmed;

    public int Id { get; init; }

    public int AcademicYearId { get; init; }
    public AcademicYear AcademicYear { get; init; } = default!;

    public int LevelId { get; init; }
    public Level Level { get; init; } = default!;

    /// <summary>Inclusive, as <see cref="Holiday.StartDate"/> is.</summary>
    public DateOnly StartDate
    {
        get => _startDate;
        init => _startDate = value;
    }

    /// <summary>Inclusive. A one-day window has <see cref="StartDate"/> equal to this.</summary>
    public DateOnly EndDate
    {
        get => _endDate;
        init => _endDate = value;
    }

    public PauseKind Kind
    {
        get => _kind;
        init => _kind = value;
    }

    /// <summary>
    /// Why the promotion is out — « Examens du premier semestre ». Required: this window moves what
    /// every stage of a promotion is measured against, and a calendar entry nobody can account for is
    /// one nobody dares remove.
    /// </summary>
    public string Reason
    {
        get => _reason;
        init => _reason = value;
    }

    /// <inheritdoc cref="ICalendarClosure.IsConfirmed"/>
    public bool IsConfirmed
    {
        get => _isConfirmed;
        init => _isConfirmed = value;
    }

    /// <summary>When the window was declared — the clock, passed in, never read from the entity.</summary>
    public DateTime RecordedOn { get; init; }

    /// <summary>
    /// The pause's own <see cref="Reason"/> is what a calendar shows for it. Implemented explicitly so
    /// the entity keeps saying « raison » while the calendar keeps saying « nom ».
    /// </summary>
    string ICalendarClosure.Name => _reason;

    public CalendarClosureScope Scope => CalendarClosureScope.Promotion;

    public int DayCount => _endDate.DayNumber - _startDate.DayNumber + 1;

    public bool Covers(DateOnly date) => date >= _startDate && date <= _endDate;

    /// <summary>Whether the window has already begun as of <paramref name="asOf"/>.</summary>
    public bool HasBegun(DateOnly asOf) => asOf >= _startDate;

    /// <summary>
    /// Whether <see cref="Correct"/> with these dates would actually move the window.
    ///
    /// <para>Asked <em>before</em> correcting because what the correction costs has to be counted while
    /// the old dates are still in place. Here rather than in the handler so that the gate and
    /// <see cref="PromotionPauseCorrection.DatesMoved"/> cannot come to disagree about what "moved"
    /// means.</para>
    /// </summary>
    public bool WouldMove(DateOnly startDate, DateOnly endDate) =>
        _startDate != startDate || _endDate != endDate;

    /// <summary>
    /// Declares the window. The rules here are the ones the promotion can decide alone; « does it
    /// overlap another window of the same promotion » is about the <i>other</i> rows and stays with
    /// <c>PromotionPauseCalendarGuard</c> — the same division as <see cref="AcademicYear"/>.
    /// </summary>
    public static Result<PromotionPause> Declare(
        Level level,
        AcademicYear year,
        DateOnly startDate,
        DateOnly endDate,
        PauseKind kind,
        string reason,
        bool isConfirmed,
        DateTime recordedOn)
    {
        string levelLabel = level.Label ?? $"niveau {level.Id}";

        // « Retrait » is a withdrawal marker, not a year of study: nobody sits exams in it and it holds
        // no stage to suspend. Same guard as the partition cut and the CNPN effectivity.
        if (!level.IsPromotion)
            return Result.Failure<PromotionPause>(LevelErrors.NotAPromotion(levelLabel));

        var shape = Validate(startDate, endDate, reason, year);
        if (shape.IsFailure)
            return Result.Failure<PromotionPause>(shape.Error);

        var pause = new PromotionPause
        {
            AcademicYearId = year.Id,
            AcademicYear = year,
            LevelId = level.Id,
            Level = level,
            StartDate = startDate,
            EndDate = endDate,
            Kind = kind,
            Reason = reason.Trim(),
            IsConfirmed = isConfirmed,
            RecordedOn = recordedOn,
        };

        pause.Raise(new PromotionPauseDeclaredDomainEvent(
            year.Id, year.Label, level.Id, levelLabel, startDate, endDate, kind, pause.Reason));

        return pause;
    }

    /// <summary>
    /// Corrects the window — most often the act of confirming dates the faculty has now settled, which
    /// is why <see cref="IsConfirmed"/> is editable rather than write-once.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Correcting a window that has already begun is allowed, and that is the case that happens.</b>
    /// An exam session declared for five days runs to six and the row has to be able to say so. Nothing
    /// was pushed when it was declared, so nothing double-counts when it moves — which is not true of
    /// <c>ResumePeriod</c>, and is why that one cannot be corrected at all.
    /// </remarks>
    public Result<PromotionPauseCorrection> Correct(
        AcademicYear year,
        DateOnly startDate,
        DateOnly endDate,
        PauseKind kind,
        string reason,
        bool isConfirmed)
    {
        var shape = Validate(startDate, endDate, reason, year);
        if (shape.IsFailure)
            return Result.Failure<PromotionPauseCorrection>(shape.Error);

        var previous = new PromotionPauseCorrection(
            _startDate, _endDate, WouldMove(startDate, endDate));

        _startDate = startDate;
        _endDate = endDate;
        _kind = kind;
        _reason = reason.Trim();
        _isConfirmed = isConfirmed;

        Raise(new PromotionPauseCorrectedDomainEvent(
            Id, AcademicYearId, LevelId, previous.PreviousStart, previous.PreviousEnd,
            startDate, endDate, previous.DatesMoved));

        return previous;
    }

    /// <summary>
    /// The rules shared by declaring and correcting. A window failing one of them is refused on both
    /// paths, for the reason <c>StudentIdentifierRules</c> exists: a rule enforced on one path only is a
    /// row that can be created and then never saved.
    /// </summary>
    private static Result Validate(DateOnly startDate, DateOnly endDate, string reason, AcademicYear year)
    {
        if (endDate < startDate)
            return Result.Failure(PromotionPauseErrors.EndsBeforeItStarts(startDate, endDate));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(PromotionPauseErrors.ReasonRequired);

        int days = endDate.DayNumber - startDate.DayNumber + 1;
        if (days > MaxSpanDays)
            return Result.Failure(PromotionPauseErrors.SpanTooLong(days, MaxSpanDays));

        // The window belongs to the year it names, so it cannot fall outside it: the promotion it
        // suspends does not exist on either side of those dates.
        if (startDate < year.StartDate || endDate > year.EndDate)
            return Result.Failure(PromotionPauseErrors.OutsideAcademicYear(
                startDate, endDate, year.Label, year.StartDate, year.EndDate));

        return Result.Success();
    }
}

/// <summary>Where a corrected window was before, and whether it actually moved.</summary>
/// <param name="DatesMoved">
/// False when only the reason, the kind or <see cref="PromotionPause.IsConfirmed"/> changed. Ticking
/// « dates confirmées » on a window already right costs no worked day, and reporting créneaux there
/// would train the reader to dismiss the one report that matters. Same gate as
/// <c>UpdateHolidayResult.DatesMoved</c>.
/// </param>
public sealed record PromotionPauseCorrection(
    DateOnly PreviousStart,
    DateOnly PreviousEnd,
    bool DatesMoved);
