using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// One academic year. It is not an attribute of the rows that name it — it <b>constitutes</b> them:
/// remove the year and an <c>AcademicGroup</c>, a <c>Registration</c> or a <c>StageSlot</c> means
/// nothing at all.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Two invariants live here, and both are relied on somewhere far away.</b></para>
///
/// <para><see cref="IsCurrent"/> is a <b>singleton the database enforces</b>
/// (<c>IX_AcademicYear_IsCurrent</c>, unique, filtered). <c>AcademicYearResolver</c> takes the first
/// row flagged current and every handler that omits a year gets it, so two rows flagged at once means
/// two screens quietly disagreeing about which promotion they show, with nothing on either to say so.
/// A year is therefore born with the flag or moved onto it by <see cref="MakeCurrent"/>, and off it
/// by <see cref="Relinquish"/> — but nothing changes it on a year already in hand.</para>
///
/// <para><b>Two years never overlap on the calendar</b>, and that is not decoration either:
/// <c>ServiceOccupancyCalculator</c> bounds a year by its <em>dates</em> rather than by
/// <c>AcademicYearId</c>, precisely because the two cannot disagree. Overlapping years would make a
/// service's load double-count every slot in the overlap. The rule is enforced by the handlers, which
/// are the only ones that can see the other years.</para>
///
/// <para>The properties carry <c>init</c> accessors over explicit backing fields rather than plain
/// setters: an object initialiser — which is how the seeder, the importer and the tests build a year —
/// still works, while nothing can reach in and change a year <em>afterwards</em> except through the
/// methods below. That is the same shape <see cref="Registration"/> uses for its outcome, and for the
/// same reason: writing the field directly is exactly how a guarded value silently loses its guard.</para>
/// </remarks>
public sealed class AcademicYear : Entity
{
    private bool _isCurrent;
    private string _label = default!;
    private DateOnly _startDate;
    private DateOnly _endDate;

    public int Id { get; set; }

    /// <summary>Human label, « 2025-2026 ». Unique across years — it is what every screen shows.</summary>
    public string Label
    {
        get => _label;
        init => _label = value;
    }

    public DateOnly StartDate
    {
        get => _startDate;
        init => _startDate = value;
    }

    public DateOnly EndDate
    {
        get => _endDate;
        init => _endDate = value;
    }

    /// <summary>« L'année en cours ». At most one row carries it, and the database says so.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        init => _isCurrent = value;
    }

    public ICollection<AcademicGroup> Groups { get; set; } = new List<AcademicGroup>();

    /// <summary>
    /// Makes this the year every unscoped handler resolves to. ⚠ The caller must have demoted the
    /// sitting year <b>first</b> — the unique index is checked per statement, so promoting before
    /// demoting is a constraint violation, not a transient state.
    /// </summary>
    public Result MakeCurrent()
    {
        if (_isCurrent)
            return Result.Failure(AcademicYearErrors.AlreadyCurrent(Label));

        _isCurrent = true;

        // ⚠ Only a year that already exists can *become* current — a year created current was never
        // anything else, and its Id is still 0 at this point, so the event would name nothing. The
        // event marks the transition every screen follows, and there is no transition here.
        if (Id != 0)
            Raise(new AcademicYearBecameCurrentDomainEvent(Id, Label));

        return Result.Success();
    }

    /// <summary>
    /// Stands down as the current year. Deliberately silent when it was not current: this is called
    /// over the whole set before a promotion, and « demote everything » must not depend on knowing
    /// which row was flagged.
    /// </summary>
    public void Relinquish() => _isCurrent = false;

    /// <summary>Renames the year. Uniqueness is the handler's to check — only it sees the others.</summary>
    public Result Rename(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return Result.Failure(AcademicYearErrors.LabelRequired);

        _label = label.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Moves the year's span. Non-overlap with the other years is the handler's to check; what is
    /// checked here is the one thing the year can know alone — that it does not end before it starts.
    /// </summary>
    public Result Reschedule(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return Result.Failure(AcademicYearErrors.EndsBeforeItStarts(startDate, endDate));

        _startDate = startDate;
        _endDate = endDate;

        return Result.Success();
    }

    /// <summary>True when <paramref name="other"/> shares at least one day with this year.</summary>
    public bool OverlapsWith(DateOnly otherStart, DateOnly otherEnd) =>
        _startDate <= otherEnd && otherStart <= _endDate;
}
