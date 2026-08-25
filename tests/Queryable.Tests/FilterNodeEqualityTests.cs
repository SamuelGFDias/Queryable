using Queryable.Filtering;
using Xunit;

namespace Queryable.Tests;

/// <summary>
/// Etapa 8 (issue #1): <see cref="FilterGroup"/> compara por valor, recursivamente, incluindo
/// <see cref="FilterGroup.Children"/> — antes, a igualdade sintetizada do <c>record</c> comparava
/// a lista de filhos por referência e duas árvores idênticas eram "diferentes".
/// </summary>
public class FilterNodeEqualityTests
{
    private static FilterNode BuildTree() =>
        new FilterGroup(FilterLogic.Or,
        [
            new FilterCondition("nome", "eq", "ana"),
            new FilterNot(new FilterGroup(FilterLogic.And,
            [
                new FilterCondition("ativo", "eq", "true"),
                new FilterCondition("id", "in", "1,2,3")
            ]))
        ]);

    [Fact]
    public void FilterGroup_ArvoresIdenticasConstruidasSeparadamente_SaoIguais()
    {
        FilterNode a = BuildTree();
        FilterNode b = BuildTree();

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void FilterGroup_ListaEArrayComMesmoConteudo_SaoIguais()
    {
        var a = new FilterGroup(FilterLogic.And, new List<FilterNode> { new FilterCondition("nome", "eq", "ana") });
        var b = new FilterGroup(FilterLogic.And, new FilterNode[] { new FilterCondition("nome", "eq", "ana") });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void FilterGroup_OrdemDosFilhosDiferente_SaoDiferentes()
    {
        var a = new FilterGroup(FilterLogic.And, [new FilterCondition("a", "eq", "1"), new FilterCondition("b", "eq", "2")]);
        var b = new FilterGroup(FilterLogic.And, [new FilterCondition("b", "eq", "2"), new FilterCondition("a", "eq", "1")]);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void FilterGroup_LogicDiferente_SaoDiferentes()
    {
        var a = new FilterGroup(FilterLogic.And, [new FilterCondition("a", "eq", "1")]);
        var b = new FilterGroup(FilterLogic.Or, [new FilterCondition("a", "eq", "1")]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FilterGroup_QuantidadeDeFilhosDiferente_SaoDiferentes()
    {
        var a = new FilterGroup(FilterLogic.And, [new FilterCondition("a", "eq", "1")]);
        var b = new FilterGroup(FilterLogic.And, [new FilterCondition("a", "eq", "1"), new FilterCondition("a", "eq", "1")]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FilterGroup_FilhoAninhadoDiferente_SaoDiferentes()
    {
        FilterNode a = BuildTree();
        FilterNode b = new FilterGroup(FilterLogic.Or,
        [
            new FilterCondition("nome", "eq", "ana"),
            new FilterNot(new FilterGroup(FilterLogic.And,
            [
                new FilterCondition("ativo", "eq", "false"),
                new FilterCondition("id", "in", "1,2,3")
            ]))
        ]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FilterGroup_Iguais_TemMesmoGetHashCode()
    {
        Assert.Equal(BuildTree().GetHashCode(), BuildTree().GetHashCode());
    }

    [Fact]
    public void FilterGroup_ServeComoChaveDeDictionaryEHashSet()
    {
        var dict = new Dictionary<FilterNode, string> { [BuildTree()] = "cacheado" };
        var set = new HashSet<FilterNode> { BuildTree() };

        Assert.True(dict.TryGetValue(BuildTree(), out string? valor));
        Assert.Equal("cacheado", valor);
        Assert.Contains(BuildTree(), set);
        Assert.False(set.Add(BuildTree()));
    }

    [Fact]
    public void FilterGroup_ComparadoComNull_EhFalso()
    {
        var a = new FilterGroup(FilterLogic.And, []);

        Assert.False(a.Equals(null));
        Assert.False(a == null);
    }

    [Fact]
    public void FilterGroup_GruposVazios_SaoIguais()
    {
        Assert.Equal(new FilterGroup(FilterLogic.And, []), new FilterGroup(FilterLogic.And, new List<FilterNode>()));
    }
}
