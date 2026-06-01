using Queryable.Core;

namespace Queryable.Extensions;

public static class PaginationExtensions
{
    public static PagedResult<T> ToPagedResult<T>(
        this List<T> items,
        int page,
        int pageSize,
        int totalCount
    )
    {
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