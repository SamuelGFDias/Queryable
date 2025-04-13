using Queryable.Core;

namespace Queryable.Interfaces;

public interface IQuerySpecApplier
{
    PagedResult<T> Apply<T>(IQueryable<T> query, QuerySpec<T> spec);
}