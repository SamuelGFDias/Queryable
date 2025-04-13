using System.Linq.Expressions;
using System.Reflection;
using Queryable.Interfaces;

namespace Queryable.Builders;

public class SortBuilder : ISortBuilder
{
    public IOrderedQueryable<T> ApplySort<T>(IQueryable<T> query, string? sortFields)
    {
        if (string.IsNullOrWhiteSpace(sortFields))
            return query.OrderBy(x => 0); // fallback neutro

        string[]? fields = sortFields.Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool isFirst = true;
        IOrderedQueryable<T>? orderedQuery = null;

        foreach (string? field in fields)
        {
            bool isDescending = field.StartsWith("-");
            string? propertyName = field.TrimStart('-');

            PropertyInfo? property = typeof(T).GetProperty(propertyName,
                                                           BindingFlags.IgnoreCase
                                                         | BindingFlags.Public
                                                         | BindingFlags.Instance);

            if (property == null)
                throw new ArgumentException($"Campo de ordenação '{propertyName}' não encontrado.");

            ParameterExpression? parameter = Expression.Parameter(typeof(T), "x");
            MemberExpression? member = Expression.Property(parameter, property);
            LambdaExpression? lambda = Expression.Lambda(member, parameter);

            string method = isFirst
                                ? (isDescending ? "OrderByDescending" : "OrderBy")
                                : (isDescending ? "ThenByDescending" : "ThenBy");

            object? result = typeof(System.Linq.Queryable).GetMethods()
                                                          .First(m => m.Name == method && m.GetParameters().Length == 2)
                                                          .MakeGenericMethod(typeof(T), property.PropertyType)
                                                          .Invoke(
                                                               null,
                                                               [isFirst ? query : orderedQuery!, lambda]);

            orderedQuery = (IOrderedQueryable<T>)result!;
            isFirst = false;
        }

        return orderedQuery!;
    }
}