using System.Linq.Expressions;
using System.Reflection;

namespace Queryable.Configuration;

/// <summary>
/// Resultado imutável da execução do construtor de uma <see cref="QueryableConfiguration{TEntity}"/>
/// para um tipo específico: os aliases configurados via <c>For(...).As(...)</c> (com o
/// <see cref="QueryableUsage"/> de cada um), os caminhos marcados via <c>Ignore(...)</c>, o flag
/// <c>OnlyMapped()</c> e as expressões de ordenação composta configuradas via
/// <c>ForSort(...).As(...)</c>. Consumido por <c>ReflectionPropertyPathProvider</c> para compor o
/// mapa final de caminhos daquele tipo.
/// </summary>
internal sealed class QueryableTypeConfiguration
{
    public QueryableTypeConfiguration(
        Type entityType,
        IReadOnlyDictionary<string, (List<PropertyInfo> Caminho, QueryableUsage Uso)> mappedAliases,
        IReadOnlyList<List<PropertyInfo>> ignoredPaths,
        bool onlyMapped,
        IReadOnlyDictionary<string, LambdaExpression> sortExpressions)
    {
        EntityType = entityType;
        MappedAliases = mappedAliases;
        IgnoredPaths = ignoredPaths;
        OnlyMapped = onlyMapped;
        SortExpressions = sortExpressions;
    }

    /// <summary>O tipo de entidade (<c>TEntity</c>) ao qual esta configuração se aplica.</summary>
    public Type EntityType { get; }

    /// <summary>
    /// Aliases explicitamente configurados via <c>For(...).As(...)</c> (ou o alias padrão, quando
    /// <c>As</c> não foi chamado), mapeando alias (case-insensitive) para a cadeia de propriedades
    /// e o uso (<see cref="QueryableUsage"/>) em que o alias fica disponível.
    /// </summary>
    public IReadOnlyDictionary<string, (List<PropertyInfo> Caminho, QueryableUsage Uso)> MappedAliases { get; }

    /// <summary>
    /// Caminhos marcados via <c>Ignore(...)</c>. Comparados estruturalmente (não por alias) contra
    /// o mapa automático, para remover corretamente qualquer alias automático que aponte para o
    /// mesmo caminho — inclusive quando a propriedade tem um alias vindo de <c>[Queryable]</c>.
    /// </summary>
    public IReadOnlyList<List<PropertyInfo>> IgnoredPaths { get; }

    /// <summary>
    /// Quando <c>true</c>, o mapa final para <see cref="EntityType"/> contém apenas os aliases de
    /// <see cref="MappedAliases"/> — toda entrada automática por reflexão é descartada.
    /// </summary>
    public bool OnlyMapped { get; }

    /// <summary>
    /// Expressões de ordenação composta configuradas via <c>ForSort(...).As(...)</c>, mapeando
    /// alias (case-insensitive) para a <see cref="LambdaExpression"/> original — sem desembrulhar,
    /// para preservar o tipo de retorno exato usado pelo <c>SortBuilder</c> ao montar
    /// <c>OrderBy</c>/<c>ThenBy</c>.
    /// </summary>
    public IReadOnlyDictionary<string, LambdaExpression> SortExpressions { get; }
}
