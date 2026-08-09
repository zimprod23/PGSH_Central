using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Hospitals;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Repartition;

internal sealed class GetLevelRepartitionQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetLevelRepartitionQuery, LevelRepartitionResponse>
{
    public async Task<Result<LevelRepartitionResponse>> Handle(
        GetLevelRepartitionQuery request, CancellationToken cancellationToken)
    {
        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Id, l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<LevelRepartitionResponse>(RepartitionErrors.LevelNotFound(request.LevelId));

        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<LevelRepartitionResponse>(year.Error);

        (int academicYearId, string yearLabel) = year.Value;

        // Both halves of the pivot are year-constituted: a cohort exists per (group, year) and a slot
        // carries that year's dates. The slot predicate is not redundant with the cohort's — it also
        // keeps a cell out of the table if its two ends ever disagree on the year, rather than
        // printing it under the wrong dates.
        var cells = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.Cohort.Stage.LevelId == request.LevelId
                     && a.Cohort.AcademicGroup.AcademicYearId == academicYearId
                     && a.StageSlot.AcademicYearId == academicYearId)
            .Select(a => new CellRow(
                a.Cohort.StageId,
                a.Cohort.Stage.Name,
                a.StageSlotId,
                a.StageSlot.PeriodNumber,
                a.StageSlot.StartDate,
                a.StageSlot.EndDate,
                a.ServiceId,
                a.Service.Name,
                a.Service.Hospital.Name,
                a.Cohort.AcademicGroup.GroupNumber,
                a.Cohort.AcademicGroup.RotationGroup))
            .ToListAsync(cancellationToken);

        var axis = PeriodAxis.Build(cells.Select(c => (c.SlotStart, c.SlotEnd)));

        if (cells.Count == 0 || axis.Count == 0)
        {
            return new LevelRepartitionResponse(
                level.Id, level.Label, level.Year, level.AcademicProgram, academicYearId, yearLabel,
                axis, [], new RepartitionSummary(0, axis.Count, 0, 0, 0));
        }

        var chefs = await ResolveChefsAsync(
            cells.Select(c => c.ServiceId).Distinct().ToList(),
            axis[0].StartDate,
            cancellationToken);

        var rows = BuildRows(cells, axis, chefs);

        int planned = rows.Sum(r => r.Cells.Count(c => c is not null));

        var summary = new RepartitionSummary(
            RowCount:     rows.Count,
            ColumnCount:  axis.Count,
            PlannedCells: planned,
            EmptyCells:   (rows.Count * axis.Count) - planned,
            GroupCount:   cells.Select(c => c.GroupNumber).Distinct().Count());

        return new LevelRepartitionResponse(
            level.Id, level.Label, level.Year, level.AcademicProgram, academicYearId, yearLabel,
            axis, rows, summary);
    }

    /// <summary>
    /// Who led each service when the planning starts, not who leads it today. A répartition reprinted
    /// three years later has to keep naming the chef it was published with — <c>ChefHistory</c> is
    /// exactly that record. Three sources, in descending order of authority:
    ///
    /// <list type="number">
    /// <item>the tenure open on <paramref name="asOf"/> — dated, and the only one that survives a reprint;</item>
    /// <item>the sitting chef, for services whose tenure trail predates the audit trail;</item>
    /// <item>the legacy note in the description (<see cref="ServiceChefSourceNote"/>) — undated, and
    /// the only name available for 140 of 148 services, none of which has a configured chef.</item>
    /// </list>
    ///
    /// The third is flagged rather than blended in: it is the name the Access base last recorded, not
    /// the name this document was published under, and only the caller can decide whether that
    /// distinction matters on the page.
    /// </summary>
    private async Task<Dictionary<int, ChefAttribution>> ResolveChefsAsync(
        List<int> serviceIds, DateOnly asOf, CancellationToken cancellationToken)
    {
        var resolved = await dbContext.Services
            .AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                AtPlanningStart = s.ChefHistory
                    .Where(h => h.StartDate <= asOf && (h.EndDate == null || h.EndDate >= asOf))
                    .OrderByDescending(h => h.StartDate)
                    .Select(h => h.Employee.FirstName + " " + h.Employee.LastName)
                    .FirstOrDefault(),
                Sitting = s.ServiceChef == null
                    ? null
                    : s.ServiceChef.FirstName + " " + s.ServiceChef.LastName,
                // Parsed in memory, not in SQL: the note is free text with a known prefix, and
                // there are at most a few hundred services on a level.
                s.Description,
            })
            .ToListAsync(cancellationToken);

        return resolved.ToDictionary(
            s => s.Id,
            s =>
            {
                string? configured = Normalize(s.AtPlanningStart) ?? Normalize(s.Sitting);
                if (configured is not null)
                    return new ChefAttribution(configured, FromSourceNote: false);

                string? fromNote = ServiceChefSourceNote.Read(s.Description);
                return new ChefAttribution(fromNote, FromSourceNote: fromNote is not null);
            });
    }

    private static string? Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    private static List<RepartitionRow> BuildRows(
        List<CellRow> cells, IReadOnlyList<PeriodWindow> axis, Dictionary<int, ChefAttribution> chefs)
    {
        var columnsBySlot = cells
            .Select(c => (c.SlotId, c.SlotStart, c.SlotEnd))
            .Distinct()
            .ToDictionary(
                s => s.SlotId,
                s => PeriodAxis.ColumnsCovered(axis, s.SlotStart, s.SlotEnd).Select(w => w.Index).ToList());

        var rows = new List<RowDraft>();

        foreach (var group in cells.GroupBy(c => (c.StageId, c.ServiceId)))
        {
            var first = group.First();
            var slots = new RepartitionCell?[axis.Count];
            var bandByColumn = new string?[axis.Count];

            foreach (var cell in group.GroupBy(c => c.SlotId))
            {
                var numbers = cell.Select(c => c.GroupNumber).Distinct().Order().ToList();
                var rendered = new RepartitionCell(
                    cell.Key, cell.First().PeriodNumber, GroupNumberRanges.Format(numbers), numbers);

                // The partition band a row prints in is the one its *first* period belongs to: a row
                // visits groups from every partition over the year, so only the opening cell says
                // which block of the rotation this line is.
                string? band = cell
                    .Select(c => c.RotationGroup)
                    .Distinct()
                    .SingleOrDefaultIfMany();

                foreach (int column in columnsBySlot[cell.Key])
                {
                    slots[column - 1] = rendered;
                    bandByColumn[column - 1] = band;
                }
            }

            int firstOccupied = Array.FindIndex(slots, c => c is not null);

            var chef = chefs.GetValueOrDefault(first.ServiceId);

            rows.Add(new RowDraft(
                new RepartitionRow(
                    first.StageId, first.StageName, first.ServiceId, first.ServiceName,
                    first.HospitalName, chef.Name, chef.FromSourceNote,
                    firstOccupied < 0 ? null : bandByColumn[firstOccupied],
                    slots),
                SortKey: firstOccupied < 0
                    ? (int.MaxValue, int.MaxValue)
                    : (firstOccupied, slots[firstOccupied]!.GroupNumbers[0])));
        }

        // Both axes of the reference document are ordered the same way: by the group numbers each
        // line opens on. Within Chirurgie the rows read 41-43, 44-46, 47-50…, and the stages
        // themselves read in the order their first period starts — Médecine (groups 1-40) before
        // Chirurgie (41-80) in the 3rd year, Chirurgie (1-20) before ANES REA (21-30) in the 6th.
        // Sorting by the same key reproduces both, and keeps the rotation cycle readable down the page.
        var stageOrder = rows
            .GroupBy(r => r.Row.StageId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.SortKey));

        return rows
            .OrderBy(r => stageOrder[r.Row.StageId].Item1)
            .ThenBy(r => stageOrder[r.Row.StageId].Item2)
            .ThenBy(r => r.Row.StageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SortKey.Item1)
            .ThenBy(r => r.SortKey.Item2)
            .Select(r => r.Row)
            .ToList();
    }

    private sealed record CellRow(
        int StageId, string StageName, int SlotId, int PeriodNumber,
        DateOnly SlotStart, DateOnly SlotEnd,
        int ServiceId, string ServiceName, string HospitalName,
        int GroupNumber, string? RotationGroup);

    private sealed record RowDraft(RepartitionRow Row, (int, int) SortKey);

    /// <summary>A chef's name and where it came from. A default instance is "nobody named", which is
    /// what <c>GetValueOrDefault</c> yields for a service that somehow escaped the lookup.</summary>
    private readonly record struct ChefAttribution(string? Name, bool FromSourceNote);
}

file static class RepartitionEnumerableExtensions
{
    /// <summary>The single element, or null when the sequence holds several — a cell whose cohorts
    /// disagree on their partition has no band to print.</summary>
    public static string? SingleOrDefaultIfMany(this IEnumerable<string?> source)
    {
        var only = source.Take(2).ToList();
        return only.Count == 1 ? only[0] : null;
    }
}
