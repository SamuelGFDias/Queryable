using Queryable.Core;
using Queryable.Filtering;

namespace Queryable.Extensions;

/// <summary>
/// Extensões para converter um <see cref="RequestQuery"/> (modelo achatado de requisição)
/// em um <see cref="QuerySpec{T}"/> pronto para aplicar sobre um <see cref="IQueryable{T}"/>.
/// </summary>
public static class RequestQueryExtensions
{
    private const char PairSeparatorSemicolon = ';';
    private const char PairSeparatorComma = ',';
    private const char KeyValueSeparator = '=';

    /// <summary>
    /// Converte o <see cref="RequestQuery"/> em um <see cref="QuerySpec{T}"/>, copiando
    /// <see cref="RequestQuery.Sort"/>, <see cref="RequestQuery.Page"/>,
    /// <see cref="RequestQuery.PageSize"/> e <see cref="RequestQuery.SkipTotalCount"/>, fazendo o
    /// parsing de <see cref="RequestQuery.QueryFilter"/> para <see cref="QuerySpec{T}.Filters"/>,
    /// e montando <see cref="QuerySpec{T}.Filter"/> a partir de
    /// <see cref="RequestQuery.Filter"/> e/ou <see cref="RequestQuery.FilterExpression"/> — ver
    /// regra de combinação abaixo.
    /// </summary>
    /// <remarks>
    /// Regras de parsing de <see cref="RequestQuery.QueryFilter"/>:
    /// <list type="bullet">
    /// <item><description><c>null</c>, vazio ou só espaços resultam em <see cref="QuerySpec{T}.Filters"/> vazio.</description></item>
    /// <item><description>Os pares são separados por <c>;</c> se a string contiver esse caractere;
    /// caso contrário, são separados por <c>,</c>. Isso permite usar o operador <c>in</c>
    /// (cujo valor é uma lista CSV) sem conflitar com o separador de pares — ex.: <c>"id__in=1,2,3;ativo=true"</c>.</description></item>
    /// <item><description>Cada par é dividido em chave/valor pelo primeiro <c>=</c> encontrado;
    /// valores contendo <c>=</c> não são truncados — ex.: <c>"nome=a=b"</c> vira chave <c>"nome"</c> e valor <c>"a=b"</c>.</description></item>
    /// <item><description>Itens vazios entre separadores (ex.: <c>"a=1,,b=2"</c>) são ignorados.</description></item>
    /// <item><description>Chave e valor recebem <see cref="string.Trim()"/> e a chave é normalizada com
    /// <see cref="string.ToLowerInvariant()"/>.</description></item>
    /// <item><description>Em caso de chave duplicada, o último valor prevalece.</description></item>
    /// <item><description>Um item sem <c>=</c> ou com chave vazia lança <see cref="ArgumentException"/>.</description></item>
    /// </list>
    /// Exemplos válidos de <see cref="RequestQuery.QueryFilter"/>: <c>"nome=João,idade__gt=18"</c>,
    /// <c>"id__in=1,2,3;ativo=true"</c>, <c>"descricao=texto com = dentro"</c>.
    /// <para>
    /// <b>Regra de combinação de <see cref="RequestQuery.Filter"/> com
    /// <see cref="RequestQuery.FilterExpression"/>.</b> <see cref="RequestQuery.FilterExpression"/>
    /// (<c>null</c>, vazia ou só espaços é tratada como ausente, sem chamar o parser) é
    /// interpretada por <see cref="FilterExpressionParser.Parse(string,FilterLimits?)"/>. Se só
    /// um dos dois (<see cref="RequestQuery.Filter"/> ou o resultado do parse de
    /// <see cref="RequestQuery.FilterExpression"/>) estiver preenchido, <see cref="QuerySpec{T}.Filter"/>
    /// recebe esse único valor sem transformação. Se os dois estiverem preenchidos, nunca um
    /// sobrescreve o outro — eles são combinados em
    /// <c>FilterGroup(FilterLogic.And, [Filter, filtro-da-expressão])</c>. Se nenhum dos dois
    /// estiver preenchido, <see cref="QuerySpec{T}.Filter"/> permanece <c>null</c>.
    /// </para>
    /// <para>
    /// <b>Limites de segurança.</b> O parse de <see cref="RequestQuery.FilterExpression"/> já
    /// valida sua própria árvore contra <paramref name="limits"/> (ver
    /// <see cref="FilterExpressionParser.Parse(string,FilterLimits?)"/>), mas a combinação com
    /// <see cref="RequestQuery.Filter"/> pode somar nós/profundidade de duas origens e superar um
    /// teto que nenhuma das duas árvores excedia isoladamente — por isso o resultado final de
    /// <see cref="QuerySpec{T}.Filter"/> é validado de novo, inteiro, antes de ser devolvido.
    /// </para>
    /// </remarks>
    /// <param name="request">O modelo achatado de requisição a converter.</param>
    /// <param name="limits">
    /// Tetos de segurança a validar (ver <see cref="FilterLimits"/>). Quando <c>null</c>, usa
    /// <see cref="FilterLimits.Default"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Quando <paramref name="request"/> é <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Quando algum item de <see cref="RequestQuery.QueryFilter"/> não tem <c>=</c> ou tem chave vazia.</exception>
    /// <exception cref="FilterExpressionSyntaxException">Quando <see cref="RequestQuery.FilterExpression"/> está preenchida e tem erro de sintaxe — ver <see cref="FilterExpressionParser"/>.</exception>
    /// <exception cref="FilterLimitExceededException">
    /// Quando <see cref="RequestQuery.FilterExpression"/> excede
    /// <see cref="FilterLimits.MaxExpressionLength"/>, ou quando a árvore final de
    /// <see cref="QuerySpec{T}.Filter"/> (após combinar <see cref="RequestQuery.Filter"/> e o
    /// resultado do parse de <see cref="RequestQuery.FilterExpression"/>) excede a profundidade,
    /// o número de nós, ou a quantidade de itens de alguma lista <c>in</c> permitidos.
    /// </exception>
    public static QuerySpec<T> ToQuerySpec<T>(this RequestQuery request, FilterLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        limits ??= FilterLimits.Default;

        var spec = new QuerySpec<T>
        {
            Sort = request.Sort,
            Page = request.Page,
            PageSize = request.PageSize,
            SkipTotalCount = request.SkipTotalCount,
            Filter = CombineFilters(request.Filter, ParseFilterExpression(request.FilterExpression, limits))
        };

        ParseQueryFilter(request.QueryFilter, spec.Filters, nameof(request));

        if (spec.Filter is not null)
            FilterLimitValidator.Validate(spec.Filter, limits);

        return spec;
    }

    private static FilterNode? ParseFilterExpression(string? filterExpression, FilterLimits limits) =>
        string.IsNullOrWhiteSpace(filterExpression) ? null : FilterExpressionParser.Parse(filterExpression, limits);

    private static FilterNode? CombineFilters(FilterNode? filter, FilterNode? filterFromExpression)
    {
        if (filter is null)
            return filterFromExpression;

        if (filterFromExpression is null)
            return filter;

        return new FilterGroup(FilterLogic.And, [filter, filterFromExpression]);
    }

    private static void ParseQueryFilter(string? queryFilter, IDictionary<string, string> filters, string paramName)
    {
        if (string.IsNullOrWhiteSpace(queryFilter))
            return;

        char pairSeparator = queryFilter.Contains(PairSeparatorSemicolon)
            ? PairSeparatorSemicolon
            : PairSeparatorComma;

        string[] pairs = queryFilter.Split(pairSeparator);

        foreach (string pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair))
                continue;

            int separatorIndex = pair.IndexOf(KeyValueSeparator);
            if (separatorIndex < 0)
                throw new ArgumentException(
                    $"Filtro inválido: o item '{pair}' não contém '=' separando chave e valor.",
                    paramName);

            string key = pair[..separatorIndex].Trim();
            string value = pair[(separatorIndex + 1)..].Trim();

            if (key.Length == 0)
                throw new ArgumentException(
                    $"Filtro inválido: o item '{pair}' tem chave vazia.",
                    paramName);

            filters[key.ToLowerInvariant()] = value;
        }
    }
}
