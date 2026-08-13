using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Stages.Repartition;

/// <summary>
/// The <i>répartition annuelle des stages</i> of one level: the table the faculty publishes before
/// any rotation runs, saying which academic groups spend which period in which service.
///
/// It is a <b>planning</b> document and nothing else — no marks, no results, no execution state. It
/// is also a pure pivot of <c>CohortSlotAssignment</c>: the same cells the schedule grid already
/// holds, turned a quarter. The grid asks "where does this cohort go each period"; this asks "which
/// groups does this service hold each period", across every stage of the level at once.
/// </summary>
public sealed record GetLevelRepartitionQuery(int LevelId, int? AcademicYearId = null)
    : IQuery<LevelRepartitionResponse>;

public sealed record LevelRepartitionResponse(
    int LevelId,
    string? LevelLabel,
    int LevelYear,
    AcademicProgram Program,
    int AcademicYearId,
    string AcademicYearLabel,
    IReadOnlyList<PeriodWindow> Columns,
    IReadOnlyList<RepartitionRow> Rows,
    RepartitionSummary Summary,
    // Period numbers whose stages declare different windows. Legitimate when stages genuinely run at
    // different lengths, a mistyped date otherwise — and nothing else in the system can tell you.
    // Kept off RepartitionSummary on purpose: that record is a bag of counts compared by value, and a
    // collection member would silently break its equality.
    IReadOnlyList<AxisDisagreement> AxisDisagreements);

/// <summary>
/// One printed line: a service, under the stage it hosts. A service appearing in two stages of the
/// same level is two rows — that is how the reference document reads, and the two are genuinely
/// different rotations.
/// </summary>
/// <param name="ChefIsFromSourceNote">
/// True when <paramref name="ChefName"/> was read off the legacy « Responsable (source) » note in the
/// service description rather than a configured chef with a dated tenure — the case for 140 of 148
/// services, none of which has a chef linked yet. The name is printed either way; the flag exists so
/// the app can say that linking a real chef would make the attribution survive a reprint.
/// </param>
public sealed record RepartitionRow(
    int StageId,
    string StageName,
    int ServiceId,
    string ServiceName,
    string HospitalName,
    string? ChefName,
    bool ChefIsFromSourceNote,
    IReadOnlyList<RepartitionCell?> Cells);

/// <summary>
/// One cell, aligned 1:1 with <see cref="LevelRepartitionResponse.Columns"/>. A stage whose period
/// spans several columns repeats its cell in each of them — exactly as the published table does —
/// carrying the same <see cref="SlotId"/> throughout, so a renderer that prefers to merge them can.
/// </summary>
/// <param name="RotationGroup">
/// The partition sitting in this cell, or null when its cohorts disagree.
///
/// ⚠ **A partition is a property of the cell and of nothing larger.** This used to hang off the row —
/// "the partition its first period belongs to" — which is not a fact about the row at all: over the
/// year the row visits every partition, because that is what a crossover *is*. Worse, it is silently
/// wrong in a way that looks deliberate: in a two-partition promotion every Médecine row opens on A
/// and every Chirurgie row on B, so a per-row band renders as a colour-by-stage key labelled
/// "Partition A / Partition B". Colour the cells and the mirror is what you see.
/// </param>
public sealed record RepartitionCell(
    int SlotId,
    int PeriodNumber,
    string Groups,
    IReadOnlyList<int> GroupNumbers,
    string? RotationGroup);

/// <summary>
/// The shape of the table, so a caller can assert it rather than assume it, plus the holes worth
/// reviewing before publication.
/// </summary>
/// <param name="DeclaredSlotCount">
/// Periods authored for this level and year, whether or not anything has been arranged into them.
/// ⚠ An empty table has <b>two</b> causes and they call for opposite actions: nobody has authored the
/// periods yet (0 here — go and lay an axis), or the periods exist and no group has been placed in them
/// (&gt; 0 — go and arrange). Reading only <see cref="RowCount"/> collapses them, which is why applying
/// an axis used to look like the apply had failed.
/// </param>
public sealed record RepartitionSummary(
    int RowCount,
    int ColumnCount,
    int PlannedCells,
    int EmptyCells,
    int GroupCount,
    int DeclaredSlotCount);
