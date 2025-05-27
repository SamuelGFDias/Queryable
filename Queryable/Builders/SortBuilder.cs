using System.Linq.Expressions;
using System.Reflection;
using Queryable.Attributes;
using Queryable.Interfaces;

namespace Queryable.Builders;

public class SortBuilder : ISortBuilder
{
    public IOrderedQueryable<T> ApplySort<T>(IQueryable<T> query, string? sortFields)
    {
        if (string.IsNullOrWhiteSpace(sortFields))
            return query.OrderBy(x => 0);

        string[] fields = sortFields.Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool isFirst = true;
        IOrderedQueryable<T>? orderedQuery = null;
        ParameterExpression parameter = Expression.Parameter(typeof(T), "x");

        Dictionary<string, List<PropertyInfo>> properties = BuildPropertyPaths<T>();

        foreach (string field in fields)
        {
            bool isDescending = field.StartsWith("-");
            string propertyName = field.TrimStart('-');

            if (!properties.TryGetValue(propertyName, out List<PropertyInfo>? path))
                throw new ArgumentException($"Campo de ordenação '{propertyName}' não encontrado.");

            Expression member = path.Aggregate<PropertyInfo, Expression>(
                parameter,
                Expression.Property);

            PropertyInfo targetProp = path.Last();
            LambdaExpression lambda = Expression.Lambda(member, parameter);

            string method = isFirst
                                ? (isDescending ? "OrderByDescending" : "OrderBy")
                                : (isDescending ? "ThenByDescending" : "ThenBy");

            object? result = typeof(System.Linq.Queryable).GetMethods()
                                                          .First(m => m.Name == method && m.GetParameters().Length == 2)
                                                          .MakeGenericMethod(typeof(T), targetProp.PropertyType)
                                                          .Invoke(
                                                               null,
                                                               [isFirst ? query : orderedQuery!, lambda]);

            orderedQuery = (IOrderedQueryable<T>)result!;
            isFirst = false;
        }

        return orderedQuery!;
    }

    private static Dictionary<string, List<PropertyInfo>> BuildPropertyPaths<T>()
    {
        var result = new Dictionary<string, List<PropertyInfo>>(StringComparer.OrdinalIgnoreCase);
        Type type = typeof(T);

        // Propriedades diretas anotadas
        foreach (PropertyInfo prop in type.GetProperties())
        {
            var attr = prop.GetCustomAttribute<QueryableAttribute>();
            if (attr == null) continue;
            string alias = attr.Alias?.ToLowerInvariant() ?? prop.Name.ToLowerInvariant();
            result[alias] = [prop];
        }

        foreach (PropertyInfo nav in type.GetProperties())
        {
            var navAttr = nav.GetCustomAttribute<QueryableAttribute>();
            if (navAttr == null)
                continue;

            string navAlias = (navAttr.Alias ?? nav.Name).ToLowerInvariant();

            foreach (PropertyInfo prop in nav.PropertyType.GetProperties())
            {
                var subAttr = prop.GetCustomAttribute<QueryableAttribute>();
                if (subAttr == null)
                    continue;

                string subAlias = (subAttr.Alias ?? prop.Name).ToLowerInvariant();
                string fullAlias = $"{navAlias}.{subAlias}";

                result[fullAlias] = [nav, prop];
            }
        }


        return result;
    }
}