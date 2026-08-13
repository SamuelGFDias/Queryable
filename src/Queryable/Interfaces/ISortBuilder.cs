namespace Queryable.Interfaces;

public interface ISortBuilder
{
    IOrderedQueryable<T> ApplySort<T>(IQueryable<T> query, string? sortFields);
}