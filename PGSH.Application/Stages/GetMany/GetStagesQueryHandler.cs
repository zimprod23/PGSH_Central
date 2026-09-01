using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.GetMany;

internal sealed class GetStagesQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetStagesQuery, PaginatedResponse<StageSummaryResponse>>
{
    public async Task<Result<PaginatedResponse<StageSummaryResponse>>> Handle(
        GetStagesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Stages.AsNoTracking().AsQueryable();

        if (request.LevelId.HasValue)
            query = query.Where(s => s.LevelId == request.LevelId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }

        var page = await query
            .OrderBy(s => s.Name)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                s => new StageSummaryResponse(
                    s.Id, s.Name, s.Coefficient, s.DurationInDays, s.Level.Label, s.RotationMode,
                    // Not a collection expression: an expression tree cannot hold one (CS9175).
                    Array.Empty<StageTextFigure>()),
                cancellationToken);

        int[] stageIds = [.. page.Items.Select(s => s.Id)];
        if (stageIds.Length == 0)
            return Result.Success(page);

        var figures = await TextFiguresQuery(dbContext, stageIds).ToListAsync(cancellationToken);

        var byStage = figures
            .GroupBy(f => f.StageId)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<StageTextFigure> (g) => [.. g
                    .OrderBy(f => f.Figure.CnpnCode)
                    .Select(f => f.Figure)]);

        return Result.Success(page with
        {
            Items = [.. page.Items.Select(s => byStage.TryGetValue(s.Id, out var texts)
                ? s with { TextFigures = texts }
                : s)]
        });
    }

    /// <summary>
    /// What each CNPN's requirement set states of the stages on this page.
    /// </summary>
    /// <remarks>
    /// Flat and keyed on the page's stage ids, then folded in memory. Expressed as a collection
    /// subquery inside the row projection the element would be a computed value carrying no key,
    /// which Npgsql cannot correlate — the shape that killed the macro plan. Named, and therefore
    /// reachable by <c>ToQueryString()</c> without a database behind it.
    /// </remarks>
    internal static IQueryable<StageTextFigureRow> TextFiguresQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> stageIds) =>
        dbContext.CurriculumStages
            .AsNoTracking()
            .Where(cs => stageIds.Contains(cs.StageId))
            .Select(cs => new StageTextFigureRow(
                cs.StageId,
                new StageTextFigure(
                    cs.Curriculum.CnpnVersionId,
                    cs.Curriculum.CnpnVersion.Code,
                    cs.Curriculum.Level.Label,
                    cs.Coefficient,
                    cs.DurationInDays)));
}

/// <summary>One text's figures, still carrying the stage they belong to so the fold can key on it.</summary>
internal sealed record StageTextFigureRow(int StageId, StageTextFigure Figure);
