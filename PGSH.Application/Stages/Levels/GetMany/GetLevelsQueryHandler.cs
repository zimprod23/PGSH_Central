using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Common.Utils;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Levels.GetMany;

internal sealed class GetLevelsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetLevelsQuery, PaginatedResponse<LevelResponse>>
{
    public async Task<Result<PaginatedResponse<LevelResponse>>> Handle(
        GetLevelsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Levels.AsNoTracking().AsQueryable();

        if (request.AcademicProgram.HasValue)
            query = query.Where(l => l.AcademicProgram == request.AcademicProgram.Value);

        // Year > 0 rather than Level.IsPromotion: the rule lives on the entity, but an unmapped
        // computed property cannot be translated to SQL, and filtering in memory here would page the
        // wrong set. The two must agree — the test holds them together.
        if (request.PromotionsOnly)
            query = query.Where(l => l.Year > 0);

        // ⚠ `Level.Label` est `string?`, et le prédicat le déréférençait sans garde. PostgreSQL
        // répond « non » sur un NULL, mais le fournisseur en mémoire **lève** — donc le défaut était
        // un test qui explose plutôt qu'un écran qui ment, ce qui est le bon sens de l'erreur mais
        // reste une garde manquante. C'est la même précaution que `StudentSearch` prend sur chacune
        // de ses colonnes, et pour la même raison.
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(l => l.Label != null && l.Label.ToLower().Contains(term));
        }

        var response = await query
            .OrderBy(l => l.Year).ThenBy(l => l.Label)
            .ToPaginatedResponseAsync(
                request.PageNumber, request.PageSize,
                l => new LevelResponse(l.Id, l.Label, l.Year, l.AcademicProgram.ToString()),
                cancellationToken);

        return Result.Success(response);
    }
}
