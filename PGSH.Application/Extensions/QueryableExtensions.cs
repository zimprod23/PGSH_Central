using Microsoft.EntityFrameworkCore;
using PGSH.SharedKernel;
using System.Linq.Expressions;

namespace PGSH.Application.Extensions;

public static class QueryableExtensions
{
    public static async Task<PaginatedResponse<TResult>> ToPaginatedResponseAsync<T, TResult>(
        this IQueryable<T> query,
        int pageNumber,
        int pageSize,
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken)
    {
        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return new PaginatedResponse<TResult>(items, pageNumber, pageSize, totalCount);
    }
}
