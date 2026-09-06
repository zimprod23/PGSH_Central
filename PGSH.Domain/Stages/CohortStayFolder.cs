namespace PGSH.Domain.Stages;

/// <summary>
/// One cell of a cohorte's grid, reduced to what deciding a stay needs.
/// </summary>
/// <param name="Id">The <c>CohortSlotAssignment</c> this cell is.</param>
/// <param name="PeriodNumber">Its column on the axis — what makes two cells consecutive.</param>
public sealed record CohortCell(
    int Id,
    int PeriodNumber,
    int ServiceId,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// A continuous rotation in <b>one</b> service, covering one cell or a whole run of them.
/// </summary>
/// <remarks>
/// It is the unit a <see cref="ServicePeriod"/> is written for, which is why the cells are carried
/// rather than counted: the période names <see cref="LeadCellId"/> on its foreign key and takes one
/// <c>ServicePeriodSlotCoverage</c> row per cell in <see cref="CellIds"/>. ⚠ Both are needed — the FK
/// answers « did this come from the grid? » and only the coverage answers « is <i>this cell</i>
/// published? », which is what every planning guard actually reads.
/// </remarks>
public sealed record CohortStay(
    IReadOnlyList<int> CellIds,
    int ServiceId,
    DateOnly StartDate,
    DateOnly EndDate)
{
    /// <summary>The cell a période born of this stay names on its foreign key: the run's first.</summary>
    public int LeadCellId => CellIds[0];
}

/// <summary>
/// Groups a cohorte's cells into the stays they represent — the single answer to « combien de
/// périodes une cohorte tient-elle, et sur quels créneaux ».
/// </summary>
/// <remarks>
/// <para>Pure — no store, no clock — like <see cref="StageScoring"/>,
/// <see cref="ServicePeriodLifecycle"/> and <c>StagePeriodFolder</c>, and extracted for the same
/// reason: it was private inside <c>SchedulePublisher</c>, and the moment a second act had to produce
/// the périodes a cohorte's member holds, the rule would have been written twice. Two copies would
/// disagree about <see cref="StageRotationMode.SingleService"/> — one giving a student <i>kₛ</i>
/// périodes where his colleagues hold one, each with its own evaluation.</para>
///
/// <para>⚠ <b>A run breaks on a gap in the column numbers <i>and</i> on a change of service.</b> The
/// second matters as much as the first: a cell edited by hand to another service is two stays, not one
/// période whose service is a lie for half its span.</para>
///
/// <para>Deriving the run from the cells rather than from a caller's window is what makes it general —
/// publishing one concurrency block and publishing the whole stage produce the same stays, because a
/// cohorte only ever holds the cells of its own run.</para>
/// </remarks>
public static class CohortStayFolder
{
    /// <summary>
    /// Folds the cells of <b>one</b> cohorte. Callers holding several group by cohorte first: a run is
    /// a fact about one roster's passage through a stage, and folding two cohortes together would
    /// merge the columns of rosters that never travelled as one.
    /// </summary>
    public static IReadOnlyList<CohortStay> Fold(
        IEnumerable<CohortCell> cells, StageRotationMode rotationMode)
    {
        var ordered = cells.OrderBy(c => c.PeriodNumber).ToList();

        if (ordered.Count == 0)
            return [];

        if (rotationMode != StageRotationMode.SingleService)
            return ordered.ConvertAll(c => new CohortStay([c.Id], c.ServiceId, c.StartDate, c.EndDate));

        var stays = new List<CohortStay>();
        var run = new List<CohortCell> { ordered[0] };

        for (int i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current  = ordered[i];

            if (current.PeriodNumber == previous.PeriodNumber + 1 && current.ServiceId == previous.ServiceId)
            {
                run.Add(current);
                continue;
            }

            stays.Add(Close(run));
            run = [current];
        }

        stays.Add(Close(run));
        return stays;

        static CohortStay Close(List<CohortCell> run) => new(
            run.ConvertAll(c => c.Id), run[0].ServiceId, run.Min(c => c.StartDate), run.Max(c => c.EndDate));
    }
}
