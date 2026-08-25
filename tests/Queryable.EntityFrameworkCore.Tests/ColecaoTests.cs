using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.EntityFrameworkCore.Services;
using Queryable.Extensions;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Etapa 9, contra SQLite in-memory: (issue #2) usar uma propriedade de coleção em filtro ou
/// ordenação dá <see cref="ArgumentException"/> clara, não <c>InvalidCastException</c>; (issue #3)
/// <c>For(c =&gt; c.Produtos.Count)</c> via configuração fluent é traduzido para SQL (subconsulta
/// correlacionada) tanto em filtro quanto em ordenação. A suíte núcleo não bastaria: LINQ to
/// Objects executa em memória o que o provider relacional talvez não traduza.
/// </summary>
public class ColecaoTests : IClassFixture<SqliteInMemoryFixture>
{
    private sealed class CategoriaConfig : QueryableConfiguration<Categoria>
    {
        public CategoriaConfig()
        {
            For(c => c.Produtos.Count).As("qtd_produtos");
        }
    }

    private readonly SqliteInMemoryFixture _fixture;
    private readonly IPagedQueryService _service;

    public ColecaoTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
        var registry = new QueryableConfigurationRegistry();
        registry.Register(new CategoriaConfig());
        var provider = new ReflectionPropertyPathProvider(registry);
        _service = new PagedQueryService(new QuerySpecApplier(new FilterBuilder(provider), new SortBuilder(provider)));
    }

    private IQueryable<Categoria> Categorias => _fixture.Context.Categorias;

    private static System.Linq.Expressions.Expression<Func<Categoria, CategoriaDto>> Projecao =>
        c => new CategoriaDto { Id = c.Id, Nome = c.Nome, QtdProdutos = c.Produtos.Count };

    // Seed: Perifericos = 3 produtos, Informatica = 2, Escritorio = 1.

    [Fact]
    public async Task Filtro_QtdProdutosGte2_TraduzParaSqlERetornaCategoriasComDoisOuMais()
    {
        var request = new RequestQuery { QueryFilter = "qtd_produtos__gte=2", Sort = "nome" };

        PagedResult<CategoriaDto> result = await _service.ApplyFilterPaginatedAsync(Categorias, request, Projecao);

        Assert.Equal(["Informatica", "Perifericos"], result.Items.Select(c => c.Nome));
        Assert.Equal(2, result.Meta.TotalCount);
    }

    [Fact]
    public async Task Ordenacao_PorQtdProdutosDesc_TraduzParaSql()
    {
        var request = new RequestQuery { Sort = "-qtd_produtos" };

        PagedResult<CategoriaDto> result = await _service.ApplyFilterPaginatedAsync(Categorias, request, Projecao);

        Assert.Equal([3, 2, 1], result.Items.Select(c => c.QtdProdutos));
    }

    [Fact]
    public async Task Filtro_EmPropriedadeDeColecao_LancaArgumentExceptionClara()
    {
        var request = new RequestQuery { QueryFilter = "produtos=x" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ApplyFilterPaginatedAsync(Categorias, request, Projecao));

        Assert.Contains("'produtos' é uma coleção", ex.Message);
        Assert.Contains("Produtos.Count", ex.Message);
    }

    [Fact]
    public async Task Ordenacao_EmPropriedadeDeColecao_LancaArgumentExceptionClara()
    {
        var request = new RequestQuery { Sort = "produtos" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ApplyFilterPaginatedAsync(Categorias, request, Projecao));

        Assert.Contains("'produtos' é uma coleção", ex.Message);
    }
}
