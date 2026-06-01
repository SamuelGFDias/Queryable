using Queryable.Core;

namespace Queryable.Interfaces;

public interface IQuerySpecApplier
{
    IQueryable<T> Apply<T>(IQueryable<T> query, QuerySpec<T> spec);
    IQueryable<T> ApplyPaged<T>(IQueryable<T> query, QuerySpec<T> spec);
}