using Xunit;
using Queryable.Builders;
using Queryable.Core;
using Queryable.Filtering;

namespace Queryable.Tests;

/// <summary>
/// Suíte da Etapa 4: integração de <see cref="QuerySpec{T}.Filter"/> (árvore de filtro
/// composto) com <see cref="QuerySpecApplier"/>, incluindo a regra de combinação com
/// <see cref="QuerySpec{T}.Filters"/> (dicionário legado) por <c>AND</c>.
/// </summary>
public class QuerySpecApplierTests
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

    private static QuerySpecApplier CriarApplier() => new(new FilterBuilder(), new SortBuilder());

    [Fact]
    public void Apply_FilterPreenchido_PermiteOrReal()
    {
        // OR era impossível de expressar via Filters (dicionário) antes desta etapa.
        var spec = new QuerySpec<Produto>
        {
            Filter = new FilterGroup(FilterLogic.Or,
            [
                new FilterCondition("nome", "eq", "Caderno"),
                new FilterCondition("nome", "eq", "Lapis")
            ])
        };

        List<Produto> resultado = CriarApplier().Apply(CriarProdutos().AsQueryable(), spec).ToList();

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Nome == "Caderno");
        Assert.Contains(resultado, p => p.Nome == "Lapis");
    }

    [Fact]
    public void Apply_FiltersEFilterPreenchidosJuntos_CombinamPorAnd()
    {
        // Filters (dicionário): ativo = true -> Caneta Azul, Lapis.
        // Filter (árvore, OR): nome = Caderno OR nome = Lapis.
        // AND dos dois: só Lapis (é ativo E está no OR de nomes).
        var spec = new QuerySpec<Produto>
        {
            Filters = new Dictionary<string, string> { ["ativo"] = "true" },
            Filter = new FilterGroup(FilterLogic.Or,
            [
                new FilterCondition("nome", "eq", "Caderno"),
                new FilterCondition("nome", "eq", "Lapis")
            ])
        };

        List<Produto> resultado = CriarApplier().Apply(CriarProdutos().AsQueryable(), spec).ToList();

        Assert.Single(resultado);
        Assert.Equal("Lapis", resultado[0].Nome);
    }

    [Fact]
    public void Apply_FilterNulo_ComportamentoIdenticoAoAnterior_SoFiltersConsiderado()
    {
        var spec = new QuerySpec<Produto>
        {
            Filters = new Dictionary<string, string> { ["ativo"] = "true" }
        };

        Assert.Null(spec.Filter);

        List<Produto> resultado = CriarApplier().Apply(CriarProdutos().AsQueryable(), spec).ToList();

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, p => Assert.True(p.Ativo));
    }

    [Fact]
    public void Apply_FiltersVazioEFilterPreenchido_ResultadoIgualAoFilterIsolado()
    {
        var specComFilterApenas = new QuerySpec<Produto>
        {
            Filter = new FilterCondition("categoria.nome", "eq", "Papelaria")
        };

        List<Produto> resultado = CriarApplier().Apply(CriarProdutos().AsQueryable(), specComFilterApenas).ToList();

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, p => Assert.Equal("Papelaria", p.Categoria.Nome));
    }

    [Fact]
    public void Apply_NenhumFiltroPreenchido_RetornaTodos()
    {
        var spec = new QuerySpec<Produto>();

        List<Produto> resultado = CriarApplier().Apply(CriarProdutos().AsQueryable(), spec).ToList();

        Assert.Equal(3, resultado.Count);
    }
}
