using Queryable.Core;

namespace Queryable.Extensions;

public static class PaginationExtensions
{
    public static PagedResult<T> ToPagedResult<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        int totalCount
    )
    {
        List<T> items = query.ToList();

        return new PagedResult<T>
        {
            Items = items,
            Meta = new PageMeta()
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
            }
        };
    }
}