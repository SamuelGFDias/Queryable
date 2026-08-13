using Queryable.Builders;
using Queryable.Filtering;
using Xunit;

namespace Queryable.Tests;

/// <summary>
/// Garante a fonte única de operadores (<see cref="FilterOperators"/>), consumida tanto pela
/// query string legada (<see cref="FilterBuilder"/>) quanto pela mini-linguagem textual
/// (<see cref="FilterExpressionParser"/>). Cobre o conteúdo exato da lista e, ponta a ponta em
/// cada consumidor, que a ordenação por comprimento decrescente é preservada — sem ela,
/// <c>campo__gte</c> seria reconhecido como operador <c>te</c> em vez de <c>gte</c>.
/// </summary>
public class FilterOperatorsTests
{
    [Fact]
    public void All_ContemExatamenteOsOitoOperadoresEsperados()
    {
        var esperados = new HashSet<string> { "eq", "gt", "lt", "gte", "lte", "contains", "in", "neq" };

        Assert.Equal(8, FilterOperators.All.Count);
        Assert.Equal(esperados, FilterOperators.All.ToHashSet());
    }

    [Fact]
    public void All_EstaOrdenadaPorComprimentoDecrescente()
    {
        List<int> tamanhos = FilterOperators.All.Select(op => op.Length).ToList();
        List<int> tamanhosOrdenados = tamanhos.OrderByDescending(t => t).ToList();

        Assert.Equal(tamanhosOrdenados, tamanhos);
    }

    [Fact]
    public void Default_EhEq()
    {
        Assert.Equal("eq", FilterOperators.Default);
    }

    [Fact]
    public void FilterBuilder_SufixoGte_EReconhecidoComoOperadorGte_NaoComoTe()
    {
        var produtos = new List<Produto>
        {
            new() { Id = Guid.NewGuid(), Nome = "Caneta", Preco = 10m },
            new() { Id = Guid.NewGuid(), Nome = "Caderno", Preco = 5m }
        }.AsQueryable();

        var builder = new FilterBuilder();
        // Preco tem [Queryable("valor")]: o alias endereçável é "valor", não "preco".
        var predicate = builder.BuildPredicate<Produto>(
            new Dictionary<string, string> { ["valor__gte"] = "10" });

        List<Produto> resultado = produtos.Where(predicate.Compile()).ToList();

        Assert.Single(resultado);
        Assert.Equal("Caneta", resultado[0].Nome);
    }

    [Fact]
    public void FilterExpressionParser_SufixoGte_EReconhecidoComoOperadorGte_NaoComoTe()
    {
        FilterNode node = FilterExpressionParser.Parse("valor__gte=10");

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("valor", condition.Field);
        Assert.Equal("gte", condition.Operator);
        Assert.Equal("10", condition.Value);
    }
}
