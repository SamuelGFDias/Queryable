using System.Linq.Expressions;
using System.Reflection;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.Builders;

public class SortBuilder : ISortBuilder
{
    // Instância padrão compartilhada, usada pelo construtor sem parâmetros, para que o
    // cache do provider também valha para consumidores que instanciam SortBuilder
    // diretamente (fora do container de DI).
    private static readonly IPropertyPathProvider DefaultPathProvider = new ReflectionPropertyPathProvider();

    private readonly IPropertyPathProvider _pathProvider;

    public SortBuilder() : this(DefaultPathProvider)
    {
    }

    public SortBuilder(IPropertyPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
    }

    public IOrderedQueryable<T> ApplySort<T>(IQueryable<T> query, string? sortFields)
    {
        if (string.IsNullOrWhiteSpace(sortFields))
            return query.OrderBy(x => 0);

        string[] fields = sortFields.Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool isFirst = true;
        IOrderedQueryable<T>? orderedQuery = null;
        ParameterExpression parameter = Expression.Parameter(typeof(T), "x");

        IReadOnlyDictionary<string, List<PropertyInfo>> properties = _pathProvider.GetPaths<T>();

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
}