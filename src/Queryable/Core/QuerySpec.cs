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

    public int Page
    {
        get => _page;
        set => _page = value > 0 ? value : _page;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > 0 ? value : _pageSize;
    }

    public bool SkipTotalCount { get; set; }
}