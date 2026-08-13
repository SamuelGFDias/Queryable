using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Queryable.Core;
using Queryable.Filtering;

namespace Queryable.Extensions;

public partial class QuerySpecModelBinder<T> : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        IQueryCollection query = bindingContext.HttpContext.Request.Query;

        bindingContext.Result = ModelBindingResult.Success(BuildSpec(query));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Monta <see cref="QuerySpec{T}"/> a partir dos pares chave/valor da query string, sem
    /// depender de <see cref="ModelBindingContext"/> — extraído de <see cref="BindModelAsync"/>
    /// para permitir testar o binder sem montar infraestrutura de ASP.NET Core (sem
    /// <c>HttpContext</c> nem <c>ModelBindingContext</c> fake).
    /// </summary>
    /// <remarks>
    /// Reconhece, case-insensitive: <c>page</c>, <c>pageSize</c>, <c>sort</c>,
    /// <c>skipTotalCount</c>, <c>filter</c> (mini-linguagem textual de filtros compostos, ver
    /// <see cref="FilterExpressionParser"/>) e o padrão <c>Filters[chave]</c> que o Swagger UI
    /// gera. Qualquer outra chave cai em <see cref="QuerySpec{T}.Filters"/>, normalizada para
    /// minúsculas. <c>filter</c> nunca cai nesse fallback — não é uma chave de filtro simples
    /// <c>campo__operador=valor</c>, e misturá-la em <see cref="QuerySpec{T}.Filters"/> geraria
    /// um filtro de campo inexistente ("filter") em tempo de execução.
    /// </remarks>
    public static QuerySpec<T> BuildSpec(IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        var spec = new QuerySpec<T>();

        foreach (var (key, value) in query)
        {
            if (key.Equals("page", StringComparison.OrdinalIgnoreCase))
            {
                spec.Page = int.TryParse(value, out int page) ? page : 1;
            } else if (key.Equals("pageSize", StringComparison.OrdinalIgnoreCase))
            {
                spec.PageSize = int.TryParse(value, out int pageSize) ? pageSize : 10;
            } else if (key.Equals("sort", StringComparison.OrdinalIgnoreCase))
            {
                spec.Sort = value;
            } else if (key.Equals("skipTotalCount", StringComparison.OrdinalIgnoreCase))
            {
                spec.SkipTotalCount = bool.TryParse(value, out bool skip) && skip;
            } else if (key.Equals("filter", StringComparison.OrdinalIgnoreCase))
            {
                // Mini-linguagem textual de filtros compostos (Queryable.Filtering.FilterExpressionParser).
                // Ausente ou vazia/só espaços: nada muda (spec.Filter permanece null). Nunca cai
                // no ramo abaixo que popula spec.Filters — "filter" não é uma chave de filtro
                // simples campo__operador=valor.
                if (!string.IsNullOrWhiteSpace(value))
                    spec.Filter = FilterExpressionParser.Parse(value!);
            } else if (SwaggerFilterRegex().Match(key) is { Success: true } match)
            {
                string cleanKey = match.Groups[1].Value;
                spec.Filters[cleanKey.ToLowerInvariant()] = value!;
            } else
            {
                spec.Filters[key.ToLowerInvariant()] = value!;
            }
        }

        return spec;
    }

    [GeneratedRegex(@"Filters\[(.*?)\]")]
    private static partial Regex SwaggerFilterRegex();
}
