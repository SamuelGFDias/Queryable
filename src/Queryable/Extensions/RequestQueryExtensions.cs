using Queryable.Core;

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
    /// <see cref="RequestQuery.PageSize"/> e <see cref="RequestQuery.SkipTotalCount"/>,
    /// e fazendo o parsing de <see cref="RequestQuery.QueryFilter"/> para
    /// <see cref="QuerySpec{T}.Filters"/>.
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
    /// </remarks>
    /// <exception cref="ArgumentNullException">Quando <paramref name="request"/> é <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Quando algum item de <see cref="RequestQuery.QueryFilter"/> não tem <c>=</c> ou tem chave vazia.</exception>
    public static QuerySpec<T> ToQuerySpec<T>(this RequestQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var spec = new QuerySpec<T>
        {
            Sort = request.Sort,
            Page = request.Page,
            PageSize = request.PageSize,
            SkipTotalCount = request.SkipTotalCount
        };

        ParseQueryFilter(request.QueryFilter, spec.Filters, nameof(request));

        return spec;
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
