using System.Linq.Expressions;
using Queryable.Interfaces;

namespace Queryable.Core;

public class QuerySpecApplier(IFilterBuilder filterBuilder, ISortBuilder sortBuilder) : IQuerySpecApplier
{
    public IQueryable<T> Apply<T>(IQueryable<T> query, QuerySpec<T> spec)
    {
        Expression<Func<T, bool>> predicate = filterBuilder.BuildPredicate<T>(spec.Filters);
        IQueryable<T> filtered = query.Where(predicate);

        IOrderedQueryable<T> ordered = sortBuilder.ApplySort(filtered, spec.Sort);

        return ordered;
    }

    public IQueryable<T> ApplyPaged<T>(IQueryable<T> query, QuerySpec<T> spec) =>
        query.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize);
}