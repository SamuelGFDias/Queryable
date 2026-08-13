using System.Text.Json;
using Xunit;
using Queryable.Filtering;

namespace Queryable.Tests;

/// <summary>
/// Suíte da Etapa 4: <see cref="FilterNodeJsonConverter"/>, o conversor JSON polimórfico que
/// resolve o tipo concreto de <see cref="FilterNode"/> por presença de campo (sem discriminador
/// <c>$type</c>). O conversor é aplicado automaticamente via
/// <c>[JsonConverter(typeof(FilterNodeJsonConverter))]</c> em <see cref="FilterNode"/>, então os
/// testes usam <see cref="JsonSerializer"/> sem registrar nada em
/// <see cref="JsonSerializerOptions"/>.
/// </summary>
public class FilterNodeJsonTests
{
    [Fact]
    public void Deserializa_CondicaoFolhaSimples()
    {
        const string json = """{ "field": "nome", "operator": "eq", "value": "ana" }""";

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("nome", condition.Field);
        Assert.Equal("eq", condition.Operator);
        Assert.Equal("ana", condition.Value);
    }

    [Fact]
    public void Deserializa_CondicaoSemOperator_AssumeEq()
    {
        const string json = """{ "field": "ativo", "value": "true" }""";

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("eq", condition.Operator);
    }

    [Fact]
    public void Deserializa_CondicaoComOperatorVazio_AssumeEq()
    {
        const string json = """{ "field": "ativo", "operator": "", "value": "true" }""";

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("eq", condition.Operator);
    }

    [Fact]
    public void Deserializa_GrupoOr_ComFilhos()
    {
        const string json = """
        {
            "logic": "or",
            "children": [
                { "field": "nome", "operator": "contains", "value": "ana" },
                { "field": "preco", "operator": "gte", "value": "100" }
            ]
        }
        """;

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.Or, group.Logic);
        Assert.Equal(2, group.Children.Count);

        var first = Assert.IsType<FilterCondition>(group.Children[0]);
        Assert.Equal("nome", first.Field);
        Assert.Equal("contains", first.Operator);
        Assert.Equal("ana", first.Value);

        var second = Assert.IsType<FilterCondition>(group.Children[1]);
        Assert.Equal("preco", second.Field);
        Assert.Equal("gte", second.Operator);
        Assert.Equal("100", second.Value);
    }

    [Fact]
    public void Deserializa_GrupoAninhado_OrContendoAnd()
    {
        const string json = """
        {
          "logic": "or",
          "children": [
            { "field": "nome", "operator": "contains", "value": "ana" },
            {
              "logic": "and",
              "children": [
                { "field": "preco", "operator": "gte", "value": "100" },
                { "field": "ativo", "value": "true" }
              ]
            }
          ]
        }
        """;

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.Or, group.Logic);
        Assert.Equal(2, group.Children.Count);

        Assert.IsType<FilterCondition>(group.Children[0]);
        var inner = Assert.IsType<FilterGroup>(group.Children[1]);
        Assert.Equal(FilterLogic.And, inner.Logic);
        Assert.Equal(2, inner.Children.Count);
    }

    [Fact]
    public void Deserializa_Not()
    {
        const string json = """{ "not": { "field": "ativo", "value": "true" } }""";

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var not = Assert.IsType<FilterNot>(node);
        var inner = Assert.IsType<FilterCondition>(not.Inner);
        Assert.Equal("ativo", inner.Field);
        Assert.Equal("true", inner.Value);
    }

    [Fact]
    public void Deserializa_LogicEmMaiusculas_Funciona()
    {
        const string json = """
        { "logic": "OR", "children": [ { "field": "nome", "value": "ana" } ] }
        """;

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.Or, group.Logic);
    }

    [Fact]
    public void Deserializa_NomesDePropriedade_CaseInsensitive()
    {
        const string json = """{ "FIELD": "nome", "Operator": "EQ", "VaLuE": "ana" }""";

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("nome", condition.Field);
        Assert.Equal("EQ", condition.Operator);
        Assert.Equal("ana", condition.Value);
    }

    [Fact]
    public void Deserializa_GrupoComNomesMaiusculos_Funciona()
    {
        const string json = """
        { "LOGIC": "and", "CHILDREN": [ { "field": "nome", "value": "ana" } ] }
        """;

        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json)!;

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.And, group.Logic);
        Assert.Single(group.Children);
    }

    [Fact]
    public void Deserializa_ObjetoSemFieldLogicChildrenOuNot_LancaJsonExceptionComMensagemClara()
    {
        const string json = """{ "foo": "bar" }""";

        JsonException ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FilterNode>(json));

        Assert.Contains("field", ex.Message);
        Assert.Contains("logic", ex.Message);
        Assert.Contains("not", ex.Message);
    }

    [Fact]
    public void Deserializa_CondicaoSemValue_LancaJsonExceptionComMensagemClara()
    {
        const string json = """{ "field": "nome", "operator": "eq" }""";

        JsonException ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FilterNode>(json));

        Assert.Contains("nome", ex.Message);
        Assert.Contains("value", ex.Message);
    }

    [Fact]
    public void Deserializa_GrupoSemChildren_LancaJsonException()
    {
        const string json = """{ "logic": "and" }""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FilterNode>(json));
    }

    [Fact]
    public void Deserializa_LogicInvalido_LancaJsonException()
    {
        const string json = """{ "logic": "xor", "children": [] }""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FilterNode>(json));
    }

    [Fact]
    public void RoundTrip_CondicaoSimples_DesserializaDeVoltaParaArvoreEquivalente()
    {
        var original = new FilterCondition("nome", "eq", "ana");

        string json = JsonSerializer.Serialize<FilterNode>(original);
        FilterNode roundTripped = JsonSerializer.Deserialize<FilterNode>(json)!;

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_ArvoreComposta_DesserializaDeVoltaParaArvoreEquivalente()
    {
        FilterNode original = new FilterGroup(FilterLogic.Or,
        [
            new FilterCondition("nome", "contains", "ana"),
            new FilterNot(new FilterCondition("ativo", "eq", "false")),
            new FilterGroup(FilterLogic.And,
            [
                new FilterCondition("preco", "gte", "100"),
                new FilterCondition("ativo", "eq", "true")
            ])
        ]);

        string json = JsonSerializer.Serialize(original);
        FilterNode roundTripped = JsonSerializer.Deserialize<FilterNode>(json)!;

        // Assert.Equal não basta aqui: a igualdade sintetizada de record para FilterGroup
        // compara `Children` (IReadOnlyList<FilterNode>) via EqualityComparer<T>.Default, que é
        // referência para List<T> — o original usa um array/list literal e o round-trip produz
        // outra List<T>, então precisa de comparação estrutural recursiva explícita.
        AssertEquivalent(original, roundTripped);
    }

    private static void AssertEquivalent(FilterNode expected, FilterNode actual)
    {
        switch (expected)
        {
            case FilterCondition expectedCondition:
                var actualCondition = Assert.IsType<FilterCondition>(actual);
                Assert.Equal(expectedCondition.Field, actualCondition.Field);
                Assert.Equal(expectedCondition.Operator, actualCondition.Operator);
                Assert.Equal(expectedCondition.Value, actualCondition.Value);
                break;

            case FilterGroup expectedGroup:
                var actualGroup = Assert.IsType<FilterGroup>(actual);
                Assert.Equal(expectedGroup.Logic, actualGroup.Logic);
                Assert.Equal(expectedGroup.Children.Count, actualGroup.Children.Count);
                for (int i = 0; i < expectedGroup.Children.Count; i++)
                    AssertEquivalent(expectedGroup.Children[i], actualGroup.Children[i]);
                break;

            case FilterNot expectedNot:
                var actualNot = Assert.IsType<FilterNot>(actual);
                AssertEquivalent(expectedNot.Inner, actualNot.Inner);
                break;

            default:
                throw new NotSupportedException($"Tipo de nó '{expected.GetType().Name}' não suportado no teste.");
        }
    }

    [Fact]
    public void Serializa_Grupo_ProduzFormatoEsperado()
    {
        FilterNode filter = new FilterGroup(FilterLogic.Or,
        [
            new FilterCondition("nome", "contains", "ana")
        ]);

        string json = JsonSerializer.Serialize(filter);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("or", root.GetProperty("logic").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("children").ValueKind);
        JsonElement child = root.GetProperty("children")[0];
        Assert.Equal("nome", child.GetProperty("field").GetString());
        Assert.Equal("contains", child.GetProperty("operator").GetString());
        Assert.Equal("ana", child.GetProperty("value").GetString());
    }
}
