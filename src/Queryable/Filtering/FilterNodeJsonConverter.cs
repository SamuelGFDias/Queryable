using System.Text.Json;
using System.Text.Json.Serialization;

namespace Queryable.Filtering;

/// <summary>
/// Conversor JSON polimórfico para <see cref="FilterNode"/>. Resolve o tipo concreto do nó por
/// <b>presença de campo</b> (sem discriminador <c>$type</c> explícito), para o JSON continuar
/// legível para quem o monta manualmente no frontend:
/// <list type="bullet">
/// <item><description>objeto com <c>field</c> ⇒ <see cref="FilterCondition"/>
/// (<c>operator</c> ausente ou vazio assume <c>"eq"</c>; <c>value</c> é obrigatório).</description></item>
/// <item><description>objeto com <c>logic</c> + <c>children</c> ⇒ <see cref="FilterGroup"/>
/// (<c>logic</c> aceita <c>"and"</c>/<c>"or"</c>, sem diferenciar maiúsculas/minúsculas).</description></item>
/// <item><description>objeto com <c>not</c> (objeto único) ⇒ <see cref="FilterNot"/>.</description></item>
/// </list>
/// Nomes de propriedade JSON são reconhecidos sem diferenciar maiúsculas/minúsculas. Um nó de
/// raiz que já é uma condição folha (<c>{ "field": ..., "value": ... }</c>, sem <c>logic</c>) é
/// aceito diretamente — não é obrigatório embrulhá-lo em um grupo.
/// </summary>
public sealed class FilterNodeJsonConverter : JsonConverter<FilterNode>
{
    public override FilterNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return ReadNode(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, FilterNode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteNode(writer, value);
    }

    private static FilterNode ReadNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException(
                $"Nó de filtro inválido: esperado um objeto JSON, mas foi recebido '{element.ValueKind}'.");

        JsonElement? fieldElement = GetPropertyIgnoreCase(element, "field");
        if (fieldElement is not null)
            return ReadCondition(element, fieldElement.Value);

        JsonElement? logicElement = GetPropertyIgnoreCase(element, "logic");
        JsonElement? childrenElement = GetPropertyIgnoreCase(element, "children");
        if (logicElement is not null || childrenElement is not null)
            return ReadGroup(logicElement, childrenElement);

        JsonElement? notElement = GetPropertyIgnoreCase(element, "not");
        if (notElement is not null)
            return new FilterNot(ReadNode(notElement.Value));

        throw new JsonException(
            "Nó de filtro inválido: esperado 'field' (condição), 'logic' + 'children' (grupo) ou 'not' (negação), " +
            "mas nenhum desses campos foi encontrado no objeto.");
    }

    private static FilterCondition ReadCondition(JsonElement element, JsonElement fieldElement)
    {
        if (fieldElement.ValueKind != JsonValueKind.String)
            throw new JsonException("Campo 'field' de uma condição de filtro precisa ser uma string.");

        string field = fieldElement.GetString()
            ?? throw new JsonException("Campo 'field' de uma condição de filtro não pode ser nulo.");

        string op = "eq";
        JsonElement? operatorElement = GetPropertyIgnoreCase(element, "operator");
        if (operatorElement is { ValueKind: not JsonValueKind.Null } opElement)
        {
            string? rawOp = opElement.GetString();
            if (!string.IsNullOrWhiteSpace(rawOp))
                op = rawOp;
        }

        JsonElement? valueElement = GetPropertyIgnoreCase(element, "value");
        if (valueElement is null || valueElement.Value.ValueKind == JsonValueKind.Null)
            throw new JsonException(
                $"Condição de filtro para o campo '{field}' está sem o campo obrigatório 'value'.");

        string value = valueElement.Value.ValueKind == JsonValueKind.String
            ? valueElement.Value.GetString()!
            : valueElement.Value.GetRawText();

        return new FilterCondition(field, op, value);
    }

    private static FilterGroup ReadGroup(JsonElement? logicElement, JsonElement? childrenElement)
    {
        if (logicElement is null)
            throw new JsonException("Grupo de filtro está sem o campo obrigatório 'logic'.");

        if (childrenElement is null)
            throw new JsonException("Grupo de filtro está sem o campo obrigatório 'children'.");

        string? rawLogic = logicElement.Value.ValueKind == JsonValueKind.String
            ? logicElement.Value.GetString()
            : null;

        FilterLogic logic = rawLogic?.ToLowerInvariant() switch
        {
            "and" => FilterLogic.And,
            "or" => FilterLogic.Or,
            _ => throw new JsonException(
                $"Campo 'logic' inválido: '{rawLogic}'. Valores aceitos: 'and' ou 'or' (sem diferenciar maiúsculas/minúsculas).")
        };

        if (childrenElement.Value.ValueKind != JsonValueKind.Array)
            throw new JsonException("Campo 'children' de um grupo de filtro precisa ser um array.");

        List<FilterNode> children = childrenElement.Value
            .EnumerateArray()
            .Select(ReadNode)
            .ToList();

        return new FilterGroup(logic, children);
    }

    private static JsonElement? GetPropertyIgnoreCase(JsonElement element, string name)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static void WriteNode(Utf8JsonWriter writer, FilterNode node)
    {
        switch (node)
        {
            case FilterCondition condition:
                writer.WriteStartObject();
                writer.WriteString("field", condition.Field);
                writer.WriteString("operator", condition.Operator);
                writer.WriteString("value", condition.Value);
                writer.WriteEndObject();
                break;

            case FilterGroup group:
                writer.WriteStartObject();
                writer.WriteString("logic", group.Logic == FilterLogic.Or ? "or" : "and");
                writer.WritePropertyName("children");
                writer.WriteStartArray();
                foreach (FilterNode child in group.Children)
                    WriteNode(writer, child);
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;

            case FilterNot not:
                writer.WriteStartObject();
                writer.WritePropertyName("not");
                WriteNode(writer, not.Inner);
                writer.WriteEndObject();
                break;

            default:
                throw new JsonException($"Tipo de nó '{node.GetType().Name}' não suportado para serialização.");
        }
    }
}
