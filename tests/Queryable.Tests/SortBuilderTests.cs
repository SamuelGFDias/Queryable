using Xunit;
using Queryable.Builders;

namespace Queryable.Tests;

public class SortBuilderTests
{
    private static List<Produto> CriarProdutos() =>
    [
        new Produto { Nome = "Caneta", Preco = 10m, Categoria = new Categoria { Nome = "Papelaria" } },
        new Produto { Nome = "Caderno", Preco = 25m, Categoria = new Categoria { Nome = "Escritorio" } },
        new Produto { Nome = "Lapis", Preco = 25m, Categoria = new Categoria { Nome = "Papelaria" } }
    ];

    [Fact]
    public void Sort_CampoSimplesAscendente_OrdenaCorretamente()
    {
        var builder = new SortBuilder();
        List<Produto> resultado = builder.ApplySort(CriarProdutos().AsQueryable(), "nome").ToList();

        Assert.Equal(["Caderno", "Caneta", "Lapis"], resultado.Select(p => p.Nome));
    }

    [Fact]
    public void Sort_CampoSimplesDescendente_OrdenaCorretamente()
    {
        var builder = new SortBuilder();
        List<Produto> resultado = builder.ApplySort(CriarProdutos().AsQueryable(), "-nome").ToList();

        Assert.Equal(["Lapis", "Caneta", "Caderno"], resultado.Select(p => p.Nome));
    }

    [Fact]
    public void Sort_MultiplosCampos_UsaSegundoCampoParaDesempatar()
    {
        // "Caderno" e "Lapis" têm Preco = 25; "Caneta" tem Preco = 10.
        // Ordenando por -valor,nome: primeiro por Preco desc, depois por Nome asc para desempate.
        var builder = new SortBuilder();
        List<Produto> resultado = builder.ApplySort(CriarProdutos().AsQueryable(), "-valor,nome").ToList();

        Assert.Equal(["Caderno", "Lapis", "Caneta"], resultado.Select(p => p.Nome));
    }

    [Fact]
    public void Sort_CampoAninhado_OrdenaPorPropriedadeDeNavegacao()
    {
        var builder = new SortBuilder();
        List<Produto> resultado = builder.ApplySort(CriarProdutos().AsQueryable(), "categoria.nome").ToList();

        Assert.Equal(["Escritorio", "Papelaria", "Papelaria"], resultado.Select(p => p.Categoria.Nome));
    }

    [Fact]
    public void Sort_PorAlias_Funciona()
    {
        var builder = new SortBuilder();
        List<Produto> resultado = builder.ApplySort(CriarProdutos().AsQueryable(), "valor").ToList();

        Assert.Equal([10m, 25m, 25m], resultado.Select(p => p.Preco));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sort_NuloOuVazio_NaoLancaEPreservaOrdemOriginal(string? sort)
    {
        List<Produto> produtos = CriarProdutos();
        var builder = new SortBuilder();

        var ordenado = builder.ApplySort(produtos.AsQueryable(), sort);
        List<Produto> resultado = ordenado.ToList();

        Assert.IsAssignableFrom<IOrderedQueryable<Produto>>(ordenado);
        Assert.Equal(produtos.Select(p => p.Nome), resultado.Select(p => p.Nome));
    }

    [Fact]
    public void Sort_CampoInexistente_LancaArgumentException()
    {
        var builder = new SortBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.ApplySort(CriarProdutos().AsQueryable(), "campoinexistente"));
    }
}
