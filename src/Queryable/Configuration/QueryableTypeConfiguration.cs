using System.Reflection;

namespace Queryable.Configuration;

/// <summary>
/// Resultado imutável da execução do construtor de uma <see cref="QueryableConfiguration{TEntity}"/>
/// para um tipo específico: os aliases configurados via <c>For(...).As(...)</c>, os caminhos
/// marcados via <c>Ignore(...)</c> e o flag <c>OnlyMapped()</c>. Consumido por
/// <c>ReflectionPropertyPathProvider</c> para compor o mapa final de caminhos daquele tipo.
/// </summary>
internal sealed class QueryableTypeConfiguration
{
    public QueryableTypeConfiguration(
        Type entityType,
        IReadOnlyDictionary<string, List<PropertyInfo>> mappedAliases,
        IReadOnlyList<List<PropertyInfo>> ignoredPaths,
        bool onlyMapped)
    {
        EntityType = entityType;
        MappedAliases = mappedAliases;
        IgnoredPaths = ignoredPaths;
        OnlyMapped = onlyMapped;
    }

    /// <summary>O tipo de entidade (<c>TEntity</c>) ao qual esta configuração se aplica.</summary>
    public Type EntityType { get; }

    /// <summary>
    /// Aliases explicitamente configurados via <c>For(...).As(...)</c> (ou o alias padrão, quando
    /// <c>As</c> não foi chamado), mapeando alias (case-insensitive) para a cadeia de propriedades.
    /// </summary>
    public IReadOnlyDictionary<string, List<PropertyInfo>> MappedAliases { get; }

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
}
