using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Queryable.Core;
using Queryable.Filtering;

namespace Queryable.Extensions;

public partial class QuerySpecModelBinder<T> : IModelBinder
{
    /// <summary>
    /// Ponto de entrada do ASP.NET Core. Resolve <see cref="FilterLimits"/> do container de DI
    /// da requisição (<c>HttpContext.RequestServices</c>), caindo para
    /// <see cref="FilterLimits.Default"/> se nada estiver registrado, e delega para
    /// <see cref="BuildSpec(IEnumerable{KeyValuePair{string,StringValues}},FilterLimits?)"/>.
    /// </summary>
    /// <remarks>
    /// <b>400 em vez de 500 (mudança de comportamento deliberada).</b>
    /// <see cref="FilterExpressionSyntaxException"/> (erro de sintaxe na mini-linguagem) e
    /// <see cref="FilterLimitExceededException"/> (árvore acima de algum teto de
    /// <see cref="FilterLimits"/>) são capturadas aqui e viram
    /// <see cref="ModelBindingContext.ModelState"/>/<see cref="ModelBindingResult.Failed"/> — com
    /// <c>[ApiController]</c>, isso vira automaticamente um <c>400</c> com a mensagem, em vez do
    /// <c>500</c> não tratado que uma exceção não capturada produziria. Nenhuma outra exceção é
    /// capturada aqui: continuam propagando sem alteração de comportamento.
    /// <see cref="BuildSpec(IEnumerable{KeyValuePair{string,StringValues}},FilterLimits?)"/> em si
    /// continua <b>lançando</b> essas exceções — só este método (a borda HTTP) as converte em
    /// erro de modelo, para que a lógica de montagem do <see cref="QuerySpec{T}"/> permaneça
    /// testável sem infraestrutura web.
    /// </remarks>
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        IQueryCollection query = bindingContext.HttpContext.Request.Query;
        FilterLimits limits = bindingContext.HttpContext.RequestServices.GetService<FilterLimits>()
            ?? FilterLimits.Default;

        try
        {
            bindingContext.Result = ModelBindingResult.Success(BuildSpec(query, limits));
        }
        catch (Exception ex) when (ex is FilterExpressionSyntaxException or FilterLimitExceededException)
        {
            bindingContext.ModelState.AddModelError(bindingContext.ModelName, ex.Message);
            bindingContext.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Monta <see cref="QuerySpec{T}"/> a partir dos pares chave/valor da query string, sem
    /// depender de <see cref="ModelBindingContext"/> — extraído de <see cref="BindModelAsync"/>
    /// para permitir testar o binder sem montar infraestrutura de ASP.NET Core (sem
    /// <c>HttpContext</c> nem <c>ModelBindingContext</c> fake). Ao contrário de
    /// <see cref="BindModelAsync"/>, este método <b>lança</b>
    /// <see cref="FilterExpressionSyntaxException"/>/<see cref="FilterLimitExceededException"/>
    /// em vez de convertê-las em erro de modelo — é o próprio <see cref="BindModelAsync"/> quem
    /// decide isso, na borda HTTP.
    /// </summary>
    /// <param name="query">Os pares chave/valor da query string.</param>
    /// <param name="limits">
    /// Tetos de segurança a validar (ver <see cref="FilterLimits"/>). Quando <c>null</c>, usa
    /// <see cref="FilterLimits.Default"/> — este método é estático e não tem acesso ao container
    /// de DI por conta própria; <see cref="BindModelAsync"/> resolve os limites configurados e os
    /// repassa explicitamente.
    /// </param>
    /// <remarks>
    /// Reconhece, case-insensitive: <c>page</c>, <c>pageSize</c>, <c>sort</c>,
    /// <c>skipTotalCount</c>, <c>filter</c> (mini-linguagem textual de filtros compostos, ver
    /// <see cref="FilterExpressionParser"/>) e o padrão <c>Filters[chave]</c> que o Swagger UI
    /// gera. Qualquer outra chave cai em <see cref="QuerySpec{T}.Filters"/>, normalizada para
    /// minúsculas. <c>filter</c> nunca cai nesse fallback — não é uma chave de filtro simples
    /// <c>campo__operador=valor</c>, e misturá-la em <see cref="QuerySpec{T}.Filters"/> geraria
    /// um filtro de campo inexistente ("filter") em tempo de execução.
    /// <para>
    /// <b>Limites de segurança.</b> O parse de <c>filter</c> já valida sua própria árvore (ver
    /// <see cref="FilterExpressionParser.Parse(string,FilterLimits?)"/>); ao final, a árvore
    /// resultante em <see cref="QuerySpec{T}.Filter"/> é validada de novo, inteira, contra
    /// <paramref name="limits"/> — defesa em profundidade caso o resultado final venha a somar
    /// nós de mais de uma origem no futuro.
    /// </para>
    /// </remarks>
    public static QuerySpec<T> BuildSpec(
        IEnumerable<KeyValuePair<string, StringValues>> query,
        FilterLimits? limits = null)
    {
        limits ??= FilterLimits.Default;

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
                    spec.Filter = FilterExpressionParser.Parse(value!, limits);
            } else if (SwaggerFilterRegex().Match(key) is { Success: true } match)
            {
                string cleanKey = match.Groups[1].Value;
                spec.Filters[cleanKey.ToLowerInvariant()] = value!;
            } else
            {
                spec.Filters[key.ToLowerInvariant()] = value!;
            }
        }

        if (spec.Filter is not null)
            FilterLimitValidator.Validate(spec.Filter, limits);

        return spec;
    }

    [GeneratedRegex(@"Filters\[(.*?)\]")]
    private static partial Regex SwaggerFilterRegex();
}
