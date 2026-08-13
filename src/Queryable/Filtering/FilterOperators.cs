namespace Queryable.Filtering;

/// <summary>
/// Fonte única dos operadores de filtro suportados (chave <c>campo__operador=valor</c>),
/// compartilhada pela query string legada (<see cref="Queryable.Builders.FilterBuilder"/>) e
/// pela mini-linguagem textual de filtros compostos (<see cref="FilterExpressionParser"/>).
/// Qualquer operador novo entra aqui — e só aqui; os dois consumidores nunca devem manter cópia
/// local da lista, sob pena de divergência silenciosa entre a query string e a mini-linguagem.
/// </summary>
/// <remarks>
/// <see cref="All"/> já vem ordenado por comprimento decrescente do texto do operador. Os dois
/// consumidores dependem dessa ordem para casar o sufixo <c>__operador</c> de uma chave: sem
/// ela, <c>gte</c> nunca seria testado antes de <c>gt</c>, e <c>campo__gte=1</c> passaria a ser
/// interpretado como campo <c>campo__g</c> com operador <c>te</c> (ou similar). Não itere sobre
/// <see cref="All"/> em outra ordem para esse fim.
/// </remarks>
public static class FilterOperators
{
    /// <summary>
    /// Todos os operadores suportados, ordenados por comprimento decrescente — ver o comentário
    /// da classe sobre por que essa ordem é significativa e não pode ser perdida.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
        new[] { "eq", "gt", "lt", "gte", "lte", "contains", "in", "neq" }
            .OrderByDescending(op => op.Length)
            .ToArray();

    /// <summary>Operador assumido quando a chave não traz sufixo <c>__operador</c>.</summary>
    public const string Default = "eq";
}
