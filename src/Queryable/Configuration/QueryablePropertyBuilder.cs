using System.Reflection;

namespace Queryable.Configuration;

/// <summary>
/// Builder fluente devolvido por <see cref="QueryableConfiguration{TEntity}.For{TProperty}"/>,
/// usado para nomear o alias público de um caminho de propriedades já extraído.
/// </summary>
/// <remarks>
/// A instância já registra o caminho sob o alias padrão (concatenação dos nomes das propriedades
/// com ponto, minúsculo) assim que é criada — <see cref="As"/> é opcional. Quando chamado, <see cref="As"/>
/// substitui o registro (remove o alias anterior e registra o novo), para que a configuração não
/// acabe com dois aliases explícitos apontando para o mesmo caminho apenas porque <c>As</c> foi
/// usado. Isso não afeta a convivência entre alias configurado e alias automático de mesmo caminho
/// (decisão de produto: o alias automático antigo continua existindo em paralelo, ver
/// <c>ReflectionPropertyPathProvider</c>) — essa convivência acontece na mesclagem com o mapa
/// automático, não aqui.
/// </remarks>
public sealed class QueryablePropertyBuilder<TEntity> where TEntity : class
{
    private readonly QueryableConfiguration<TEntity> _configuracao;
    private readonly List<PropertyInfo> _caminho;
    private string _aliasAtual;
    private QueryableUsage _uso = QueryableUsage.All;

    internal QueryablePropertyBuilder(
        QueryableConfiguration<TEntity> configuracao,
        List<PropertyInfo> caminho,
        string aliasPadrao)
    {
        _configuracao = configuracao;
        _caminho = caminho;
        _aliasAtual = aliasPadrao;
        _configuracao.RegistrarAlias(_aliasAtual, _caminho);
    }

    /// <summary>
    /// Define o alias público sob o qual <c>FilterBuilder</c>/<c>SortBuilder</c> resolvem este
    /// caminho de propriedades.
    /// </summary>
    /// <param name="alias">O alias. Não pode ser nulo, vazio ou composto só de espaços.</param>
    /// <exception cref="ArgumentException">Quando <paramref name="alias"/> é nulo, vazio ou espaço em branco.</exception>
    public QueryablePropertyBuilder<TEntity> As(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("O alias informado para As() não pode ser nulo ou vazio.", nameof(alias));

        string normalizado = alias.ToLowerInvariant();

        if (!string.Equals(_aliasAtual, normalizado, StringComparison.OrdinalIgnoreCase))
            _configuracao.RemoverAlias(_aliasAtual);

        // Preserva o uso já definido por FilterOnly()/SortOnly() ao renomear o alias.
        _configuracao.RegistrarAlias(normalizado, _caminho, _uso);
        _aliasAtual = normalizado;

        return this;
    }

    /// <summary>
    /// Restringe este alias a filtro — ele não aparece no mapa consultado por <c>SortBuilder</c>
    /// (<see cref="Queryable.Interfaces.IPropertyPathProvider.GetPaths{T}(QueryableUsage)"/> com
    /// <see cref="QueryableUsage.Sort"/>). Encadeável com <see cref="As"/> em qualquer ordem;
    /// chamar <see cref="SortOnly"/> depois substitui esta restrição (last-write-wins).
    /// </summary>
    public QueryablePropertyBuilder<TEntity> FilterOnly()
    {
        _uso = QueryableUsage.Filter;
        _configuracao.DefinirUso(_aliasAtual, _uso);

        return this;
    }

    /// <summary>
    /// Restringe este alias a ordenação — ele não aparece no mapa consultado por
    /// <c>FilterBuilder</c> (<see cref="Queryable.Interfaces.IPropertyPathProvider.GetPaths{T}(QueryableUsage)"/>
    /// com <see cref="QueryableUsage.Filter"/>). Encadeável com <see cref="As"/> em qualquer ordem;
    /// chamar <see cref="FilterOnly"/> depois substitui esta restrição (last-write-wins).
    /// </summary>
    public QueryablePropertyBuilder<TEntity> SortOnly()
    {
        _uso = QueryableUsage.Sort;
        _configuracao.DefinirUso(_aliasAtual, _uso);

        return this;
    }
}
