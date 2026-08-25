using Queryable.Filtering;

namespace Queryable.Core;

public class QuerySpec<T>
{
    private int _page = 1;
    private int _pageSize = 10;
    public Dictionary<string, string> Filters { get; set; } = new();

    /// <summary>
    /// Árvore de filtro composto opcional (OR, agrupamento, NOT), normalmente populada a partir
    /// da porta JSON via <see cref="FilterNodeJsonConverter"/>. Valor padrão: <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Regra de combinação com <see cref="Filters"/> (aplicada por
    /// <see cref="Queryable.Core.QuerySpecApplier"/>): se <see cref="Filter"/> for <c>null</c>, o
    /// comportamento é idêntico ao atual — só <see cref="Filters"/> é considerado. Se ambos
    /// estiverem preenchidos, o predicado final é <c>AND(Filters, Filter)</c> — os dois conjuntos
    /// de condições se combinam por <c>AND</c>, nunca um sobrescreve o outro.
    /// </remarks>
    public FilterNode? Filter { get; set; }

    public string? Sort { get; set; }

    /// <summary>
    /// Número da página, começando em 1. Valor padrão: <c>1</c>.
    /// </summary>
    /// <remarks>
    /// Atribuições menores ou iguais a zero são <b>descartadas em silêncio</b>: o valor anterior é
    /// mantido, nenhuma exceção é lançada. Decisão de design (clamp de paginação), não um
    /// descuido — <c>?page=0</c> e <c>?page=-5</c> devolvem a página 1 com status 200. Não
    /// revalide isso na borda.
    /// </remarks>
    public int Page
    {
        get => _page;
        set => _page = value > 0 ? value : _page;
    }

    /// <summary>
    /// Quantidade de itens por página. Valor padrão: <c>10</c>.
    /// </summary>
    /// <remarks>
    /// Atribuições menores ou iguais a zero são <b>descartadas em silêncio</b>, como em
    /// <see cref="Page"/>. O teto superior não é aplicado pelo setter: é
    /// <see cref="FilterLimits.MaxPageSize"/>, verificado pelos pontos de entrada que resolvem
    /// <see cref="FilterLimits"/> (<c>QuerySpecModelBinder&lt;T&gt;.BuildSpec</c>,
    /// <c>RequestQueryExtensions.ToQuerySpec&lt;T&gt;</c>, <c>PagedQueryService</c>), lançando
    /// <see cref="FilterLimitExceededException"/> quando excedido. Um <see cref="QuerySpec{T}"/>
    /// montado à mão e aplicado direto por <c>IQuerySpecApplier</c> não passa por essa checagem.
    /// </remarks>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > 0 ? value : _pageSize;
    }

    public bool SkipTotalCount { get; set; }
}