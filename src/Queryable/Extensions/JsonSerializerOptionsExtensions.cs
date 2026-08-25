using System.Text.Json;
using System.Text.Json.Serialization;
using Queryable.Filtering;

namespace Queryable.Extensions;

/// <summary>
/// Extensões de <see cref="JsonSerializerOptions"/> para levar os <see cref="FilterLimits"/>
/// configurados na aplicação até a porta JSON de filtros compostos.
/// </summary>
public static class JsonSerializerOptionsExtensions
{
    /// <summary>
    /// Registra em <see cref="JsonSerializerOptions.Converters"/> um
    /// <see cref="FilterNodeJsonConverter"/> construído com <paramref name="limits"/>, para que a
    /// desserialização de <see cref="FilterNode"/> (ex.: <c>[FromBody] QuerySpec&lt;T&gt;</c> ou
    /// <c>RequestQuery.Filter</c>) valide contra os limites configurados em vez de
    /// <see cref="FilterLimits.Default"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Funciona porque o <c>System.Text.Json</c> dá precedência a um conversor presente em
    /// <see cref="JsonSerializerOptions.Converters"/> sobre o <c>[JsonConverter]</c> anotado no
    /// tipo — é o atributo em <see cref="FilterNode"/> que, sozinho, força a instanciação sem
    /// argumentos (e portanto os defaults).
    /// </para>
    /// <para>
    /// Idempotente: qualquer <see cref="FilterNodeJsonConverter"/> já presente em
    /// <see cref="JsonSerializerOptions.Converters"/> é removido antes de inserir o novo, então
    /// chamar duas vezes deixa exatamente um conversor, com os limites da última chamada.
    /// </para>
    /// <para>
    /// O pacote núcleo não referencia o framework ASP.NET Core, então a ligação com as
    /// <c>JsonOptions</c> do MVC ou da Minimal API é feita pelo consumidor, resolvendo o
    /// <see cref="FilterLimits"/> do container:
    /// <code>
    /// // MVC
    /// services.AddOptions&lt;Microsoft.AspNetCore.Mvc.JsonOptions&gt;()
    ///     .Configure&lt;FilterLimits&gt;((o, l) =&gt; o.JsonSerializerOptions.AddQueryableFilterLimits(l));
    /// // Minimal API
    /// services.AddOptions&lt;Microsoft.AspNetCore.Http.Json.JsonOptions&gt;()
    ///     .Configure&lt;FilterLimits&gt;((o, l) =&gt; o.SerializerOptions.AddQueryableFilterLimits(l));
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="options">As opções a configurar.</param>
    /// <param name="limits">Os tetos a aplicar na desserialização de <see cref="FilterNode"/>.</param>
    /// <returns>A própria <paramref name="options"/>, para permitir encadeamento.</returns>
    /// <exception cref="ArgumentNullException">Quando <paramref name="options"/> ou <paramref name="limits"/> é <c>null</c>.</exception>
    public static JsonSerializerOptions AddQueryableFilterLimits(this JsonSerializerOptions options, FilterLimits limits)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(limits);

        for (int i = options.Converters.Count - 1; i >= 0; i--)
        {
            if (options.Converters[i] is FilterNodeJsonConverter)
                options.Converters.RemoveAt(i);
        }

        options.Converters.Add(new FilterNodeJsonConverter(limits));
        return options;
    }
}
