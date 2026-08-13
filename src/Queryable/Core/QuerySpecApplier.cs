using System.Linq.Expressions;
using Queryable.Interfaces;

namespace Queryable.Core;

public class QuerySpecApplier(IFilterBuilder filterBuilder, ISortBuilder sortBuilder) : IQuerySpecApplier
{
    /// <summary>
    /// Aplica filtro e ordenação de <paramref name="spec"/> sobre <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    /// Regra de combinação de filtro: se <see cref="QuerySpec{T}.Filter"/> for <c>null</c>, o
    /// comportamento é idêntico ao anterior a esta árvore — só <see cref="QuerySpec{T}.Filters"/>
    /// é considerado. Se ambos estiverem preenchidos, o predicado final é
    /// <c>AND(Filters, Filter)</c> (ver <see cref="IFilterBuilder.BuildPredicate{T}(IDictionary{string,string},Queryable.Filtering.FilterNode?)"/>).
    /// </remarks>
    public IQueryable<T> Apply<T>(IQueryable<T> query, QuerySpec<T> spec)
    {
        Expression<Func<T, bool>> predicate = filterBuilder.BuildPredicate<T>(spec.Filters, spec.Filter);
        IQueryable<T> filtered = query.Where(predicate);

        IOrderedQueryable<T> ordered = sortBuilder.ApplySort(filtered, spec.Sort);

        return ordered;
    }

    public IQueryable<T> ApplyPaged<T>(IQueryable<T> query, QuerySpec<T> spec) =>
        query.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize);
}