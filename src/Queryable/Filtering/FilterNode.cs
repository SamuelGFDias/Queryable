using System.Text.Json.Serialization;

namespace Queryable.Filtering;

/// <summary>
/// Nó da árvore de filtro composto. Representação intermediária única entre as portas de
/// entrada (dicionário legado <c>Dictionary&lt;string,string&gt;</c>, a porta JSON via
/// <see cref="FilterNodeJsonConverter"/> e, no futuro, a mini-linguagem de query string) e o
/// compilador que produz a <see cref="System.Linq.Expressions.Expression"/> final consumida por
/// <see cref="Queryable.Builders.FilterBuilder"/>.
/// </summary>
/// <remarks>
/// Anotado com <see cref="JsonConverterAttribute"/> apontando para
/// <see cref="FilterNodeJsonConverter"/> para que a desserialização polimórfica funcione sem
/// exigir registro manual do conversor em <see cref="System.Text.Json.JsonSerializerOptions"/>
/// do chamador.
/// </remarks>
[JsonConverter(typeof(FilterNodeJsonConverter))]
public abstract record FilterNode;

/// <summary>
/// Condição folha da árvore. <paramref name="Field"/> já vem resolvido (sem o sufixo
/// <c>__operador</c> usado pelo formato de dicionário), <paramref name="Operator"/> já vem
/// separado (ex.: <c>"eq"</c>, <c>"gt"</c>, <c>"in"</c>) e <paramref name="Value"/> é a string
/// bruta a ser convertida para o tipo da propriedade alvo pelo compilador.
/// </summary>
public sealed record FilterCondition(string Field, string Operator, string Value) : FilterNode;

/// <summary>
/// Operador lógico de combinação dos filhos de um <see cref="FilterGroup"/>.
/// </summary>
public enum FilterLogic
{
    And,
    Or
}

/// <summary>
/// Agrupamento de nós filhos combinados por <see cref="FilterLogic.And"/> ou
/// <see cref="FilterLogic.Or"/>.
/// </summary>
public sealed record FilterGroup(FilterLogic Logic, IReadOnlyList<FilterNode> Children) : FilterNode;

/// <summary>
/// Negação lógica do nó interno.
/// </summary>
public sealed record FilterNot(FilterNode Inner) : FilterNode;
