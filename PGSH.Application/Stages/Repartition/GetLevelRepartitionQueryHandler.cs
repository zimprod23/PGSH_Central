using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Hospitals.Chefs;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Repartition;

internal sealed class GetLevelRepartitionQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ServiceChefProvider chefProvider)
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

        // Read from the slots themselves rather than from the cells: a period nobody has been placed in
        // yet still has dates, and a drift there is exactly what the admin wants to hear about before
        // arranging on top of it.
        var declaredSlots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.Stage.LevelId == request.LevelId && s.AcademicYearId == academicYearId)
            .Select(s => new { s.PeriodNumber, StageName = s.Stage.Name, s.StartDate, s.EndDate })
            .ToListAsync(cancellationToken);

        var disagreements = PeriodAxisDiagnostics.Find(
            declaredSlots.Select(s => (s.PeriodNumber, s.StageName, s.StartDate, s.EndDate)));

        // The axis is every window the level *declares*, not only the windows something has been placed
        // in. A period authored but not yet arranged is still a period, and printing the table without
        // its column is what made a freshly applied axis read as though the apply had failed.
        // The cells' own windows are unioned in rather than assumed to be a subset: a cell's cohort is
        // what ties it to this level, so a slot reached through some other stage would otherwise take
        // its column — and its cells — out of the table entirely.
        var axis = PeriodAxis.Build(
            declaredSlots.Select(s => (s.StartDate, s.EndDate))
                .Concat(cells.Select(c => (c.SlotStart, c.SlotEnd))));

        if (cells.Count == 0 || axis.Count == 0)
        {
            return new LevelRepartitionResponse(
                level.Id, level.Label, level.Year, level.AcademicProgram, academicYearId, yearLabel,
                axis, [], new RepartitionSummary(0, axis.Count, 0, 0, 0, declaredSlots.Count),
                disagreements);
        }

        // Who led each service when the planning starts, not who leads it today: a répartition
        // reprinted three years later has to keep naming the chef it was published with, and
        // ChefHistory is exactly that record.
        //
        // ⚠ InForce is SourceNoteOnly today — the two ServiceChefAssignment rows in the base
        // are test links — so the name printed is the undated import note and ChefIsFromSourceNote
        // is true wherever one is printed. Narrowed here rather than on the export alone, or the
        // two documents of one faculty would name different people for one service.
        var chefs = await chefProvider.BuildAsync(
            cells.Select(c => c.ServiceId).Distinct().ToList(),
            ServiceChefPolicy.InForce,
            cancellationToken);

        var rows = BuildRows(cells, axis, chefs, axis[0].StartDate);

        int planned = rows.Sum(r => r.Cells.Count(c => c is not null));

        var summary = new RepartitionSummary(
            RowCount:     rows.Count,
            ColumnCount:  axis.Count,
            PlannedCells: planned,
            EmptyCells:   (rows.Count * axis.Count) - planned,
            GroupCount:   cells.Select(c => c.GroupNumber).Distinct().Count(),
            DeclaredSlotCount: declaredSlots.Count);

        return new LevelRepartitionResponse(
            level.Id, level.Label, level.Year, level.AcademicProgram, academicYearId, yearLabel,
            axis, rows, summary, disagreements);
    }

    private static List<RepartitionRow> BuildRows(
        List<CellRow> cells, IReadOnlyList<PeriodWindow> axis, ServiceChefDirectory chefs, DateOnly asOf)
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

            foreach (var cell in group.GroupBy(c => c.SlotId))
            {
                var numbers = cell.Select(c => c.GroupNumber).Distinct().Order().ToList();

                // The partition rides on the cell, because that is the only thing it is a fact about.
                // A row visits every partition over the year — that is what the crossover is — so a
                // row-level band could only ever mean "the one it opens on", which in a two-partition
                // promotion coincides exactly with the stage and reads as a colour-by-stage key.
                var rendered = new RepartitionCell(
                    cell.Key,
                    cell.First().PeriodNumber,
                    GroupNumberRanges.Format(numbers),
                    numbers,
                    cell.Select(c => c.RotationGroup).Distinct().SingleOrDefaultIfMany());

                foreach (int column in columnsBySlot[cell.Key])
                    slots[column - 1] = rendered;
            }

            int firstOccupied = Array.FindIndex(slots, c => c is not null);

            var chef = chefs.For(first.ServiceId, asOf);

            rows.Add(new RowDraft(
                new RepartitionRow(
                    first.StageId, first.StageName, first.ServiceId, first.ServiceName,
                    first.HospitalName, chef.Name, chef.FromSourceNote,
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
