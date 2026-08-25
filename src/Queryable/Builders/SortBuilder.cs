using System.Linq.Expressions;
using System.Reflection;
using Queryable.Configuration;
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

        IReadOnlyDictionary<string, List<PropertyInfo>> properties = _pathProvider.GetPaths<T>(QueryableUsage.Sort);
        IReadOnlyDictionary<string, LambdaExpression> sortExpressions = _pathProvider.GetSortExpressions<T>();

        foreach (string field in fields)
        {
            bool isDescending = field.StartsWith("-");
            string propertyName = field.TrimStart('-');

            LambdaExpression lambda;
            Type keyType;

            if (sortExpressions.TryGetValue(propertyName, out LambdaExpression? expressaoConfigurada))
            {
                // Alias de ForSort(...).As(...): a LambdaExpression configurada é usada como veio
                // (sem Expression.Property), preservando o parâmetro original — o tipo da chave é
                // o tipo de retorno da própria expressão.
                lambda = expressaoConfigurada;
                keyType = expressaoConfigurada.ReturnType;
            }
            else
            {
                if (!properties.TryGetValue(propertyName, out List<PropertyInfo>? path))
                    throw new ArgumentException($"Campo de ordenação '{propertyName}' não encontrado.");

                // Mesma guarda de FilterBuilder.CompileCondition (issue #2): sem ela, OrderBy<T, List<X>>
                // falha na reflexão/tradução com erro obscuro.
                if (PathExtension.IsCollectionType(path[^1].PropertyType))
                    throw new ArgumentException(PathExtension.CollectionNotScalarMessage(propertyName, "ordenação"));

                Expression member = path.Aggregate<PropertyInfo, Expression>(
                    parameter,
                    Expression.Property);

                PropertyInfo targetProp = path.Last();
                lambda = Expression.Lambda(member, parameter);
                keyType = targetProp.PropertyType;
            }

            string method = isFirst
                                ? (isDescending ? "OrderByDescending" : "OrderBy")
                                : (isDescending ? "ThenByDescending" : "ThenBy");

            orderedQuery = InvokeOrderMethod(isFirst ? query : orderedQuery!, lambda, keyType, method);
            isFirst = false;
        }

        return orderedQuery!;
    }

    /// <summary>
    /// Invoca <c>OrderBy</c>/<c>OrderByDescending</c>/<c>ThenBy</c>/<c>ThenByDescending</c> de
    /// <see cref="System.Linq.Queryable"/> por reflexão (necessário por causa do sombreamento do
    /// namespace raiz <c>Queryable</c> — ver CLAUDE.md, seção "Colisão de namespace"). Extraído em
    /// método próprio para ser compartilhado pelo caminho de propriedade simples (<c>Expression.Property</c>)
    /// e pelo caminho de expressão de ordenação composta (<c>ForSort</c>), que só diferem em como
    /// a <see cref="LambdaExpression"/> e o tipo da chave são obtidos.
    /// </summary>
    private static IOrderedQueryable<T> InvokeOrderMethod<T>(
        IQueryable<T> source,
        LambdaExpression lambda,
        Type keyType,
        string method)
    {
        object? result = typeof(System.Linq.Queryable).GetMethods()
                                                      .First(m => m.Name == method && m.GetParameters().Length == 2)
                                                      .MakeGenericMethod(typeof(T), keyType)
                                                      .Invoke(null, [source, lambda]);

        return (IOrderedQueryable<T>)result!;
    }
}