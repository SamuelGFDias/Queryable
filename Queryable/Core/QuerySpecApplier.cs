using System.Linq.Expressions;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.Core;

public class QuerySpecApplier(IFilterBuilder filterBuilder, ISortBuilder sortBuilder) : IQuerySpecApplier
{
    public PagedResult<T> Apply<T>(IQueryable<T> source, QuerySpec<T> spec)
    {
        Expression<Func<T, bool>> predicate = filterBuilder.BuildPredicate<T>(spec.Filters);
        IQueryable<T> filtered = source.Where(predicate);

        int totalCount = spec.SkipTotalCount ? 0 : filtered.Count();

        IOrderedQueryable<T> ordered = sortBuilder.ApplySort(filtered, spec.Sort);

        int pageSize = spec.PageSize > 0 ? spec.PageSize : 10;
        int page = spec.Page > 0 ? spec.Page : 1;

        IQueryable<T> paged = ordered.Skip((page - 1) * pageSize)
                                     .Take(pageSize);

        return paged.ToPagedResult(page, pageSize, totalCount);
    }
}