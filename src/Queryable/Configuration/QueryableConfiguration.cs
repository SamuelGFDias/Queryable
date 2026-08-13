using System.Linq.Expressions;
using System.Reflection;

namespace Queryable.Configuration;

/// <summary>
/// Classe base para configuração fluente de mapeamento de <typeparamref name="TEntity"/>: permite
/// declarar aliases (inclusive colapsando value objects, ex.: <c>Usuario.Cpf.Value</c> → <c>cpf</c>)
/// e restringir o que é endereçável por filtro/ordenação, sem anotar a entidade de domínio com
/// <see cref="Queryable.Attributes.QueryableAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// As chamadas a <see cref="For{TProperty}"/>, <see cref="Ignore{TProperty}"/> e
/// <see cref="OnlyMapped"/> devem ocorrer no construtor da classe derivada — a configuração é
/// executada uma única vez, na inicialização da aplicação (ver
/// <c>QueryableConfigurationRegistry</c> e <c>ServiceCollectionExtensions.AddQueryableConfiguration</c>),
/// e o resultado é imutável a partir daí.
/// </para>
/// <para>
/// <b>Modo permissivo (padrão).</b> Configurar um tipo apenas acrescenta/sobrescreve aliases —
/// tudo que já era endereçável por reflexão continua sendo, até <see cref="OnlyMapped"/> ser
/// chamado para este tipo. Ao configurar <c>For(u =&gt; u.Cpf.Value).As("cpf")</c>, o alias
/// automático antigo (<c>cpf.value</c>) continua existindo em paralelo ao novo (<c>cpf</c>) — isso
/// permite migrar o contrato HTTP sem quebrar clientes existentes num deploy só. A mesclagem final
/// com o mapa automático acontece em <c>ReflectionPropertyPathProvider</c>, não nesta classe.
/// </para>
/// </remarks>
public abstract class QueryableConfiguration<TEntity> : IQueryableConfigurationSource where TEntity : class
{
    private readonly Dictionary<string, List<PropertyInfo>> _aliasesMapeados = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<List<PropertyInfo>> _caminhosIgnorados = [];
    private bool _somenteMapeados;

    /// <summary>
    /// Inicia a configuração de um caminho de propriedades a partir de um seletor, ex.:
    /// <c>For(u =&gt; u.Cpf.Value)</c>. Sem chamar <see cref="QueryablePropertyBuilder{TEntity}.As"/>
    /// em seguida, o caminho é registrado sob o alias padrão (concatenação dos nomes das
    /// propriedades com ponto, minúsculo — ex.: <c>cpf.value</c>).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Quando <paramref name="selector"/> não é uma cadeia de propriedades (chamada de método,
    /// indexador ou campo) — a mensagem cita a expressão recebida.
    /// </exception>
    protected QueryablePropertyBuilder<TEntity> For<TProperty>(Expression<Func<TEntity, TProperty>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        List<PropertyInfo> caminho = PropertyPathExtractor.Extract(selector, nameof(For));
        string aliasPadrao = AliasPadrao(caminho);

        return new QueryablePropertyBuilder<TEntity>(this, caminho, aliasPadrao);
    }

    /// <summary>
    /// Remove do mapa final o(s) alias(es) automático(s) cujo caminho corresponda ao de
    /// <paramref name="selector"/> — comparação estrutural pelo caminho de propriedades, não pelo
    /// texto do alias (assim <c>Ignore(p =&gt; p.Preco)</c> remove o alias automático mesmo que ele
    /// venha de <c>[Queryable("valor")]</c>, não de <c>"preco"</c>).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Quando <paramref name="selector"/> não é uma cadeia de propriedades válida.
    /// </exception>
    protected void Ignore<TProperty>(Expression<Func<TEntity, TProperty>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        List<PropertyInfo> caminho = PropertyPathExtractor.Extract(selector, nameof(Ignore));
        _caminhosIgnorados.Add(caminho);
    }

    /// <summary>
    /// Ativa o modo opt-in para <typeparamref name="TEntity"/>: o mapa final passa a conter
    /// <b>apenas</b> os aliases declarados via <see cref="For{TProperty}"/> — toda entrada
    /// automática por reflexão é descartada para este tipo. Decisão por tipo: outros tipos, com ou
    /// sem configuração fluente própria, não são afetados.
    /// </summary>
    protected void OnlyMapped() => _somenteMapeados = true;

    /// <summary>
    /// Registra (ou sobrescreve) um alias explícito. Chamado pelo construtor de
    /// <see cref="QueryablePropertyBuilder{TEntity}"/> e por <see cref="QueryablePropertyBuilder{TEntity}.As"/>.
    /// </summary>
    internal void RegistrarAlias(string alias, List<PropertyInfo> caminho) =>
        _aliasesMapeados[alias] = caminho;

    /// <summary>Remove um alias explícito previamente registrado (usado quando <c>As</c> renomeia o alias padrão).</summary>
    internal void RemoverAlias(string alias) =>
        _aliasesMapeados.Remove(alias);

    private static string AliasPadrao(List<PropertyInfo> caminho) =>
        string.Join(".", caminho.Select(p => p.Name.ToLowerInvariant()));

    Type IQueryableConfigurationSource.EntityType => typeof(TEntity);

    QueryableTypeConfiguration IQueryableConfigurationSource.Build() =>
        new(
            typeof(TEntity),
            new Dictionary<string, List<PropertyInfo>>(_aliasesMapeados, StringComparer.OrdinalIgnoreCase),
            _caminhosIgnorados.ToList(),
            _somenteMapeados);
}
