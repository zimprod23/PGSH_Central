using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Curricula.Compare;

internal sealed class CompareCurriculaQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<CompareCurriculaQuery, CurriculumComparisonResponse>
{
    private sealed record Side(int Id, string VersionLabel, string? LevelLabel, Dictionary<int, Entry> Stages);
    private sealed record Entry(string Name, int Coefficient, int DurationInDays);

    public async Task<Result<CurriculumComparisonResponse>> Handle(
        CompareCurriculaQuery request, CancellationToken cancellationToken)
    {
        var from = await LoadAsync(request.LevelId, request.FromCnpnVersionId, cancellationToken);
        if (from is null)
            return Result.Failure<CurriculumComparisonResponse>(
                CurriculumErrors.NotFound(request.LevelId, request.FromCnpnVersionId));

        var to = await LoadAsync(request.LevelId, request.ToCnpnVersionId, cancellationToken);
        if (to is null)
            return Result.Failure<CurriculumComparisonResponse>(
                CurriculumErrors.NotFound(request.LevelId, request.ToCnpnVersionId));

        var entries = new List<CurriculumDiffEntry>();

        foreach (int stageId in from.Stages.Keys.Union(to.Stages.Keys))
        {
            from.Stages.TryGetValue(stageId, out var before);
            to.Stages.TryGetValue(stageId, out var after);

            var change = (before, after) switch
            {
                (null, not null) => CurriculumChange.Added,
                (not null, null) => CurriculumChange.Removed,
                (not null, not null) when before.Coefficient != after.Coefficient
                                       || before.DurationInDays != after.DurationInDays
                    => CurriculumChange.Reweighted,
                _ => CurriculumChange.Unchanged,
            };

            entries.Add(new CurriculumDiffEntry(
                stageId,
                before?.Name ?? after!.Name,
                change,
                before?.Coefficient,
                after?.Coefficient,
                before?.DurationInDays,
                after?.DurationInDays));
        }

        // Changes first — a diff is read for what moved, not for what stayed.
        var ordered = entries
            .OrderBy(e => e.Change == CurriculumChange.Unchanged)
            .ThenBy(e => e.Change)
            .ThenBy(e => e.StageName)
            .ToList();

        return new CurriculumComparisonResponse(
            request.LevelId,
            from.LevelLabel,
            request.FromCnpnVersionId,
            from.VersionLabel,
            request.ToCnpnVersionId,
            to.VersionLabel,
            ordered.Any(e => e.Change != CurriculumChange.Unchanged),
            ordered);
    }

    private async Task<Side?> LoadAsync(int levelId, int cnpnVersionId, CancellationToken ct)
    {
        var row = await dbContext.Curriculums
            .AsNoTracking()
            .Where(c => c.LevelId == levelId && c.CnpnVersionId == cnpnVersionId)
            .Select(c => new
            {
                c.Id,
                VersionLabel = c.CnpnVersion.Label,
                LevelLabel = c.Level.Label,
                Stages = c.Stages
                    .Select(s => new { s.StageId, s.Stage.Name, s.Coefficient, s.DurationInDays })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new Side(
                row.Id,
                row.VersionLabel,
                row.LevelLabel,
                row.Stages.ToDictionary(s => s.StageId, s => new Entry(s.Name, s.Coefficient, s.DurationInDays)));
    }
}
