using Microsoft.EntityFrameworkCore;
using PGSH.SharedKernel;
using System.Linq.Expressions;

namespace PGSH.Application.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Hard ceiling on rows a single page can return, regardless of the requested
    /// <c>pageSize</c>. A guardrail against a client requesting an unbounded page and
    /// hammering the database. The response still carries the true <c>TotalCount</c>,
    /// so callers can page through everything — nothing is silently hidden.
    /// </summary>
    public const int MaxPageSize = 200;

    public static async Task<PaginatedResponse<TResult>> ToPaginatedResponseAsync<T, TResult>(
        this IQueryable<T> query,
        int pageNumber,
        int pageSize,
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken)
    {
        pageNumber = pageNumber < 1 ? 1 : pageNumber;
        pageSize = pageSize < 1 ? 1 : pageSize > MaxPageSize ? MaxPageSize : pageSize;

        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return new PaginatedResponse<TResult>(items, pageNumber, pageSize, totalCount);
    }
}
