using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.RotationCycle;

/// <summary>
/// The rotation blocks a promotion is currently laid out on — so reopening the screen shows the
/// configuration that is actually in force instead of an empty form.
/// </summary>
/// <remarks>
/// <para>⚠ <b>A block is read from the axis, not from what somebody typed.</b> Stages of the level
/// whose slots carry the identical list of windows <em>are</em> a block: that is the definition the
/// whole feature rests on, and it stays true when a date is corrected on one stage's own grid
/// afterwards. Reading the last request back instead would show the axis as authored months ago and
/// quietly disagree with the grid underneath it.</para>
///
/// <para>The one thing the axis cannot state is <c>kₛ</c> — every stage of a block carries a slot on
/// every column, which is exactly what makes the crossover possible — so it is recovered, in order:
/// the apply's own audit entry, then the widest run any cohort actually holds, then nothing.
/// <see cref="RotationPeriodsSource"/> says which, because « 1 période » deduced from an empty grid
/// and « 1 période » as authored are not the same claim.</para>
/// </remarks>
public sealed record GetRotationCycleQuery(int LevelId, int? AcademicYearId = null)
    : IQuery<RotationCycleConfiguration>;

/// <summary>How <see cref="RotationBlockStage.Periods"/> was learned.</summary>
public enum RotationPeriodsSource
{
    /// <summary>Read back from the apply that authored the axis — the number the admin entered.</summary>
    Authored,

    /// <summary>Deduced from the widest run a cohort holds. Right whenever the block has been arranged.</summary>
    Derived,

    /// <summary>Neither available: the axis exists but nothing has been arranged on it and no apply is
    /// on record. The form has to be filled again.</summary>
    Unknown,
}

public sealed record RotationBlockStage(
    int StageId, string Name, int Periods, RotationPeriodsSource PeriodsSource);

/// <summary>
/// One block: the stages sharing an axis, in the order they were authored, and the axis itself.
/// </summary>
public sealed record RotationBlockConfiguration(
    IReadOnlyList<RotationBlockStage> Stages,
    IReadOnlyList<DateWindow> Windows,
    int Columns,
    // When the axis was last written. Null when no apply is on record — a block laid down before this
    // was recorded, or one built stage by stage on the slots screen.
    DateTime? AppliedAt,
    // Applying again would replace this axis, and it cannot while anything on it has been published.
    int PublishedCells);

public sealed record RotationCycleConfiguration(
    int LevelId,
    string LevelLabel,
    int AcademicYearId,
    IReadOnlyList<RotationBlockConfiguration> Blocks);

internal sealed class GetRotationCycleQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
    : IQueryHandler<GetRotationCycleQuery, RotationCycleConfiguration>
{
    public async Task<Result<RotationCycleConfiguration>> Handle(
        GetRotationCycleQuery request, CancellationToken cancellationToken)
    {
        var year = await yearResolver.ResolveAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<RotationCycleConfiguration>(year.Error);

        int yearId = year.Value;

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Label, l.Year, l.AcademicProgram })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<RotationCycleConfiguration>(RegistrationErrors.MissingLevel);

        string levelLabel = level.Label ?? $"Année {level.Year} — {level.AcademicProgram}";

        var slots = await dbContext.StageSlots
            .AsNoTracking()
            .Where(s => s.Stage.LevelId == request.LevelId && s.AcademicYearId == yearId)
            .OrderBy(s => s.PeriodNumber)
            .Select(s => new { s.Id, s.StageId, s.Stage.Name, s.PeriodNumber, s.StartDate, s.EndDate })
            .ToListAsync(cancellationToken);

        if (slots.Count == 0)
            return Result.Success(new RotationCycleConfiguration(request.LevelId, levelLabel, yearId, []));

        // The widest run a cohort actually holds in a stage — kₛ, once the block has been arranged.
        // Contiguity is not tested: a partition's run is contiguous by construction, and counting
        // cells is what survives a cell edited by hand.
        var cellsPerCohort = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.StageSlot.Stage.LevelId == request.LevelId
                     && a.StageSlot.AcademicYearId == yearId)
            .GroupBy(a => new { a.StageSlot.StageId, a.CohortId })
            .Select(g => new { g.Key.StageId, Cells = g.Count() })
            .ToListAsync(cancellationToken);

        var derivedPeriods = cellsPerCohort
            .GroupBy(x => x.StageId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Cells));

        var publishedCellIds = await dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.StageSlot.Stage.LevelId == request.LevelId
                     && a.StageSlot.AcademicYearId == yearId)
            .Select(a => new { a.Id, a.StageSlot.StageId })
            .ToListAsync(cancellationToken);

        var publishedIdSet = await dbContext.PublishedAmongAsync(
            publishedCellIds.Select(c => c.Id).ToList(), cancellationToken);

        var publishedByStage = publishedCellIds
            .Where(c => publishedIdSet.Contains(c.Id))
            .GroupBy(c => c.StageId)
            .ToDictionary(g => g.Key, g => g.Count());

        var applies = await ReadAppliesAsync(request.LevelId, cancellationToken);

        // Stages sharing an identical axis are one block. The signature is the window list itself, so
        // a stage whose dates were nudged afterwards correctly falls out of the block rather than
        // being reported as still aligned with it.
        var blocks = slots
            .GroupBy(s => s.StageId)
            .Select(g => new
            {
                StageId = g.Key,
                Name = g.First().Name,
                Windows = g.OrderBy(s => s.PeriodNumber)
                    .Select(s => new DateWindow(s.StartDate, s.EndDate))
                    .ToList(),
            })
            .GroupBy(x => string.Join("|", x.Windows.Select(w => $"{w.StartDate:O}~{w.EndDate:O}")))
            .Select(block =>
            {
                var stageIds = block.Select(x => x.StageId).ToHashSet();
                var apply = applies.FirstOrDefault(a => a.StageIds.SetEquals(stageIds));

                // Authored order first — it is the order partition A actually walks the block in, so a
                // form reopened after a reload has to show it back the same way. Falls back to the id
                // order the picker itself lists.
                var ordered = apply is null
                    ? block.OrderBy(x => x.StageId).Select(x => x.StageId).ToList()
                    : apply.Stages.Select(s => s.StageId)
                        .Concat(block.Select(x => x.StageId))
                        .Distinct()
                        .Where(stageIds.Contains)
                        .ToList();

                var stages = ordered
                    .Select(id =>
                    {
                        var stage = block.First(x => x.StageId == id);
                        int? authored = apply?.Stages.FirstOrDefault(s => s.StageId == id)?.Periods;

                        return authored is { } a
                            ? new RotationBlockStage(id, stage.Name, a, RotationPeriodsSource.Authored)
                            : derivedPeriods.TryGetValue(id, out int d)
                                ? new RotationBlockStage(id, stage.Name, d, RotationPeriodsSource.Derived)
                                : new RotationBlockStage(id, stage.Name, 1, RotationPeriodsSource.Unknown);
                    })
                    .ToList();

                var windows = block.First().Windows;

                return new RotationBlockConfiguration(
                    stages,
                    windows,
                    windows.Count,
                    apply?.AppliedAt,
                    stageIds.Sum(id => publishedByStage.GetValueOrDefault(id)));
            })
            // Widest block first: a level's main block is the one covering the year, and a stray stage
            // left on its own axis should not open the form.
            .OrderByDescending(b => b.Stages.Count)
            .ThenByDescending(b => b.Columns)
            .ToList();

        return Result.Success(new RotationCycleConfiguration(request.LevelId, levelLabel, yearId, blocks));
    }

    private sealed record AppliedBlock(
        DateTime AppliedAt, HashSet<int> StageIds, IReadOnlyList<RotationStage> Stages);

    /// <summary>
    /// Every apply recorded for this level, newest first. ⚠ Not filtered by year: the metadata carries
    /// the <em>request's</em> year, which is null whenever the caller left it to the resolver, so
    /// filtering on it would drop exactly the ordinary case. The stage set is matched against the axis
    /// on disk instead, which is a stronger check anyway — an apply for another year cannot match a
    /// block that is not there.
    /// </summary>
    private async Task<List<AppliedBlock>> ReadAppliesAsync(int levelId, CancellationToken ct)
    {
        string entityId = levelId.ToString();

        var rows = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == "ROTATION_CYCLE_APPLIED" && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.CreatedAt, a.Metadata })
            .Take(50)
            .ToListAsync(ct);

        var applies = new List<AppliedBlock>();

        foreach (var row in rows)
        {
            if (row.Metadata is null) continue;

            // An audit entry is a record of what was asked, not a contract. A malformed or
            // older-shaped one costs the form its prefill, never the request.
            try
            {
                var payload = JsonSerializer.Deserialize<AppliedPayload>(
                    row.Metadata, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload?.Stages is not { Count: > 0 }) continue;

                applies.Add(new AppliedBlock(
                    row.CreatedAt,
                    payload.Stages.Select(s => s.StageId).ToHashSet(),
                    payload.Stages));
            }
            catch (JsonException)
            {
                // Nothing to recover; the derived fallback still answers.
            }
        }

        return applies;
    }

    private sealed record AppliedPayload(List<RotationStage>? Stages);
}
