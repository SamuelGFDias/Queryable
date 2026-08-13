using Xunit;
using Queryable.Builders;
using Queryable.Filtering;

namespace Queryable.Tests;

/// <summary>
/// Suíte da camada nova introduzida na Etapa 2: o compilador <c>FilterNode -&gt; Expression</c>,
/// exercitado via <see cref="FilterBuilder.BuildPredicate{T}(FilterNode)"/>. Usa os mesmos
/// modelos e a mesma técnica (<c>List&lt;T&gt;.AsQueryable()</c>) que
/// <see cref="FilterBuilderTests"/>, focando em AND/OR/NOT e aninhamento — o que o formato
/// <c>Dictionary&lt;string,string&gt;</c> nunca conseguiu expressar.
/// </summary>
public class FilterNodeCompilerTests
{
    private static readonly Guid Produto1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Produto2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Produto3Id = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static List<Produto> CriarProdutos() =>
    [
        new Produto
        {
            Id = Produto1Id,
            Nome = "Caneta Azul",
            Preco = 10m,
            Ativo = true,
            Status = Status.Ativo,
            Categoria = new Categoria { Id = 1, Nome = "Papelaria" }
        },
        new Produto
        {
            Id = Produto2Id,
            Nome = "Caderno",
            Preco = 25m,
            Ativo = false,
            Status = Status.Inativo,
            Categoria = new Categoria { Id = 2, Nome = "Escritorio" }
        },
        new Produto
        {
            Id = Produto3Id,
            Nome = "Lapis",
            Preco = 5m,
            Ativo = true,
            Status = Status.Pendente,
            Categoria = new Categoria { Id = 1, Nome = "Papelaria" }
        }
    ];

    private static List<Produto> Filtrar(FilterNode filter)
    {
        var builder = new FilterBuilder();
        var predicate = builder.BuildPredicate<Produto>(filter);
        return CriarProdutos().AsQueryable().Where(predicate).ToList();
    }

    [Fact]
    public void FilterGroup_And_ComDuasCondicoes_ExigeAsDuasVerdadeiras()
    {
        var filter = new FilterGroup(FilterLogic.And,
        [
            new FilterCondition("ativo", "eq", "true"),
            new FilterCondition("categoria.nome", "eq", "Papelaria")
        ]);

        List<Produto> resultado = Filtrar(filter);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, p => Assert.True(p.Ativo));
        Assert.All(resultado, p => Assert.Equal("Papelaria", p.Categoria.Nome));
    }

    [Fact]
    public void FilterGroup_Or_BastaUmaCondicaoVerdadeira()
    {
        var filter = new FilterGroup(FilterLogic.Or,
        [
            new FilterCondition("nome", "eq", "Caderno"),
            new FilterCondition("nome", "eq", "Lapis")
        ]);

        List<Produto> resultado = Filtrar(filter);

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Nome == "Caderno");
        Assert.Contains(resultado, p => p.Nome == "Lapis");
    }

    [Fact]
    public void FilterNot_NegaOResultadoDaCondicaoInterna()
    {
        var filter = new FilterNot(new FilterCondition("ativo", "eq", "true"));

        List<Produto> resultado = Filtrar(filter);

        Assert.Single(resultado);
        Assert.Equal("Caderno", resultado[0].Nome);
    }

    [Fact]
    public void Aninhamento_OrDentroDeAnd_RespeitaAgrupamento()
    {
        // (nome contains "a" or nome contains "e") and ativo = true
        // "Caneta Azul" (contains "a"), "Caderno" (contains "a" e "e", mas inativo),
        // "Lapis" (contains "a"). Só ativos: Caneta Azul e Lapis.
        var filter = new FilterGroup(FilterLogic.And,
        [
            new FilterGroup(FilterLogic.Or,
            [
                new FilterCondition("nome", "contains", "an"),
                new FilterCondition("nome", "contains", "ap")
            ]),
            new FilterCondition("ativo", "eq", "true")
        ]);

        List<Produto> resultado = Filtrar(filter);

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Nome == "Caneta Azul");
        Assert.Contains(resultado, p => p.Nome == "Lapis");
        Assert.All(resultado, p => Assert.True(p.Ativo));
    }

    [Fact]
    public void FilterGroup_AndVazio_PredicadoSempreVerdadeiro()
    {
        var filter = new FilterGroup(FilterLogic.And, []);

        List<Produto> resultado = Filtrar(filter);

        Assert.Equal(3, resultado.Count);
    }

    [Fact]
    public void FilterGroup_OrVazio_PredicadoSempreFalso()
    {
        // Decisão de design da Etapa 2: grupo OR sem filhos é o elemento neutro de uma
        // disjunção sem termos, então compila para "sempre falso" (nenhuma condição para
        // ser verdadeira) — o oposto do grupo AND vazio.
        var filter = new FilterGroup(FilterLogic.Or, []);

        List<Produto> resultado = Filtrar(filter);

        Assert.Empty(resultado);
    }

    [Fact]
    public void CampoDesconhecido_LancaArgumentException_ComMesmaMensagemDoDicionario()
    {
        var filter = new FilterCondition("campoinexistente", "eq", "x");

        ArgumentException ex = Assert.Throws<ArgumentException>(() => Filtrar(filter));

        Assert.Equal("Campo 'campoinexistente' não é pesquisável.", ex.Message);
    }

    [Fact]
    public void FilterCondition_Isolada_NoNivelRaiz_FuncionaSemGrupo()
    {
        var filter = new FilterCondition("nome", "eq", "Lapis");

        List<Produto> resultado = Filtrar(filter);

        Assert.Single(resultado);
        Assert.Equal("Lapis", resultado[0].Nome);
    }
}
