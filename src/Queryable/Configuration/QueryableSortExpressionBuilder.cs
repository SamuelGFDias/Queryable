using System.Linq.Expressions;

namespace Queryable.Configuration;

/// <summary>
/// Builder fluente devolvido por <see cref="QueryableConfiguration{TEntity}.ForSort{TKey}"/>, usado
/// para nomear o alias público de uma expressão de ordenação composta (ex.: coalescência de duas
/// propriedades).
/// </summary>
/// <remarks>
/// Diferente de <see cref="QueryablePropertyBuilder{TEntity}"/> (devolvido por <c>For</c>), esta
/// instância <b>não</b> registra alias nenhum ao ser criada — <see cref="As"/> é obrigatório.
/// <c>ForSort</c> aceita qualquer <c>Expression&lt;Func&lt;TEntity, TKey&gt;&gt;</c>, inclusive uma
/// que não seja uma simples cadeia de propriedades (ex.: <c>p =&gt; p.NomeSocial ?? p.Nome</c>), e
/// não há como derivar um alias textual padrão de uma expressão arbitrária — por isso
/// <see cref="QueryableConfiguration{TEntity}.Build"/> lança <see cref="InvalidOperationException"/>
/// se um <c>ForSort(...)</c> ficar sem <see cref="As"/> correspondente.
/// </remarks>
public sealed class QueryableSortExpressionBuilder<TEntity> where TEntity : class
{
    private readonly QueryableConfiguration<TEntity> _configuracao;
    private readonly LambdaExpression _expressao;
    private string? _aliasAtual;

    internal QueryableSortExpressionBuilder(QueryableConfiguration<TEntity> configuracao, LambdaExpression expressao)
    {
        _configuracao = configuracao;
        _expressao = expressao;
    }

    /// <summary>
    /// Define o alias público sob o qual <c>SortBuilder</c> resolve esta expressão de ordenação.
    /// Chamar novamente substitui o registro anterior (remove o alias antigo e registra o novo).
    /// </summary>
    /// <param name="alias">O alias. Não pode ser nulo, vazio ou composto só de espaços.</param>
    /// <exception cref="ArgumentException">Quando <paramref name="alias"/> é nulo, vazio ou espaço em branco.</exception>
    public QueryableSortExpressionBuilder<TEntity> As(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("O alias informado para As() não pode ser nulo ou vazio.", nameof(alias));

        string normalizado = alias.ToLowerInvariant();

        if (_aliasAtual is not null && !string.Equals(_aliasAtual, normalizado, StringComparison.OrdinalIgnoreCase))
            _configuracao.RemoverExpressaoOrdenacao(_aliasAtual);

        _configuracao.RegistrarExpressaoOrdenacao(normalizado, _expressao);
        _aliasAtual = normalizado;

        return this;
    }

    /// <summary>Indica se <see cref="As"/> já foi chamado nesta instância.</summary>
    internal bool TemAlias => _aliasAtual is not null;
}
