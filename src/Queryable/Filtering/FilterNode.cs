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
/// <remarks>
/// <b>Igualdade estrutural.</b> A igualdade sintetizada pelo compilador para <c>record</c> usa
/// <see cref="EqualityComparer{T}.Default"/> em cada membro — e para <see cref="Children"/>
/// (uma <see cref="IReadOnlyList{T}"/>, normalmente <c>List&lt;T&gt;</c> ou array) isso é
/// comparação por <i>referência</i>, então duas árvores idênticas seriam "diferentes" e não
/// serviriam como chave de dicionário. Por isso <see cref="Equals(FilterGroup)"/> e
/// <see cref="GetHashCode"/> são sobrescritos para comparar <see cref="Logic"/> e os filhos
/// <b>elemento a elemento, na ordem</b> (a ordem importa: <c>[a, b]</c> ≠ <c>[b, a]</c>).
/// <see cref="FilterCondition"/> e <see cref="FilterNot"/> já têm igualdade por valor de fábrica,
/// então a recursão pelos filhos acontece pelo próprio <c>Equals</c> deles. Custo O(n) no número
/// de nós; a recursão é limitada pela profundidade da árvore, que as portas externas já
/// restringem via <see cref="FilterLimits.MaxDepth"/> — uma árvore montada em código sem limite
/// é responsabilidade do chamador.
/// </remarks>
public sealed record FilterGroup(FilterLogic Logic, IReadOnlyList<FilterNode> Children) : FilterNode
{
    /// <inheritdoc cref="FilterGroup"/>
    public bool Equals(FilterGroup? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (Logic != other.Logic || Children.Count != other.Children.Count)
            return false;

        for (int i = 0; i < Children.Count; i++)
        {
            if (!Equals(Children[i], other.Children[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Logic);
        hash.Add(Children.Count);
        foreach (FilterNode child in Children)
            hash.Add(child);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Negação lógica do nó interno.
/// </summary>
public sealed record FilterNot(FilterNode Inner) : FilterNode;
