using System.Linq.Expressions;
using global::Microsoft.EntityFrameworkCore;
using Queryable.Builders;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.EntityFrameworkCore.Services;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Suíte de <see cref="PagedQueryService"/> rodando contra SQLite in-memory (provider
/// relacional de verdade), não <c>List&lt;T&gt;.AsQueryable()</c>. O objetivo é pegar erros
/// de tradução para LINQ-to-SQL que a avaliação em memória nunca revelaria. O
/// <see cref="TestDbContext"/> está configurado para lançar em vez de cair silenciosamente em
/// avaliação no cliente, então basta nunca usar <c>AsEnumerable()</c> antes das asserções.
/// </summary>
public class PagedQueryServiceTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;
    private readonly IPagedQueryService _service;

    public PagedQueryServiceTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
        _service = new PagedQueryService(new QuerySpecApplier(new FilterBuilder(), new SortBuilder()));
    }

    private IQueryable<Produto> Produtos => _fixture.Context.Produtos;

    // 1. Filtro simples via RequestQuery.QueryFilter ("ativo=true") — retorna só os ativos.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComFiltroSimples_RetornaSomenteAtivos()
    {
        var request = new RequestQuery { QueryFilter = "ativo=true", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(4, result.Items.Count);
        Assert.All(result.Items, dto => Assert.Contains(dto.Id, new[]
        {
            SqliteInMemoryFixture.MouseId,
            SqliteInMemoryFixture.TecladoId,
            SqliteInMemoryFixture.NotebookId,
            SqliteInMemoryFixture.CaboHdmiId
        }));
    }

    // 2. Filtro com operador (preco__gte=100).
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComFiltroDeOperador_RespeitaOLimiteInferior()
    {
        var request = new RequestQuery { QueryFilter = "preco__gte=100", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(5, result.Items.Count);
        Assert.DoesNotContain(result.Items, dto => dto.Id == SqliteInMemoryFixture.CaboHdmiId);
        Assert.All(result.Items, dto => Assert.True(dto.Preco >= 100m));
    }

    // 3. Filtro em navegação (categoria.nome=Perifericos) — prova que o JOIN é traduzido.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComFiltroEmNavegacao_TraduzJoin()
    {
        var request = new RequestQuery { QueryFilter = "categoria.nome=Perifericos", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, dto => Assert.Equal("Perifericos", dto.CategoriaNome));
    }

    // 4. Ordenação (Sort = "-preco") — valida a ordem dos itens da página.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComOrdenacaoDescendente_OrdenaPorPreco()
    {
        var request = new RequestQuery { Sort = "-preco", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(
            [
                SqliteInMemoryFixture.NotebookId,
                SqliteInMemoryFixture.MonitorId,
                SqliteInMemoryFixture.ImpressoraId,
                SqliteInMemoryFixture.TecladoId,
                SqliteInMemoryFixture.MouseId,
                SqliteInMemoryFixture.CaboHdmiId
            ],
            result.Items.Select(i => i.Id));
    }

    // 5. Paginação — Page=2, PageSize=2 devolve os itens corretos e Meta coerente.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComPaginacao_RetornaPaginaEMetaCorretos()
    {
        var request = new RequestQuery { Sort = "-preco", Page = 2, PageSize = 2 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(
            [SqliteInMemoryFixture.ImpressoraId, SqliteInMemoryFixture.TecladoId],
            result.Items.Select(i => i.Id));

        Assert.Equal(2, result.Meta.Page);
        Assert.Equal(2, result.Meta.PageSize);
        Assert.Equal(6, result.Meta.TotalCount);
        Assert.Equal(3, result.Meta.TotalPages);
        Assert.True(result.Meta.HasPrevious);
        Assert.True(result.Meta.HasNext);
    }

    // 6. Projeção explícita — o PagedResult<ProdutoDto> vem preenchido, inclusive
    // CategoriaNome vindo da navegação.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComProjecaoExplicita_PreencheCategoriaNomeDaNavegacao()
    {
        var request = new RequestQuery { QueryFilter = "nome=Notebook", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        ProdutoDto item = Assert.Single(result.Items);
        Assert.Equal(SqliteInMemoryFixture.NotebookId, item.Id);
        Assert.Equal("Notebook", item.Nome);
        Assert.Equal(3500m, item.Preco);
        Assert.Equal("Informatica", item.CategoriaNome);
    }

    // 7. Sobrecarga IProjectable — mesma consulta usando a sobrecarga sem parâmetro de
    // projeção, resultado equivalente.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComSobrecargaIProjectable_ProduzResultadoEquivalente()
    {
        var request = new RequestQuery { QueryFilter = "nome=Notebook", PageSize = 10 };

        PagedResult<ProdutoDto> comProjecaoExplicita = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        PagedResult<ProdutoProjectableDto> comIProjectable =
            await _service.ApplyFilterPaginatedAsync<Produto, ProdutoProjectableDto>(Produtos, request);

        ProdutoDto esperado = Assert.Single(comProjecaoExplicita.Items);
        ProdutoProjectableDto obtido = Assert.Single(comIProjectable.Items);

        Assert.Equal(esperado.Id, obtido.Id);
        Assert.Equal(esperado.Nome, obtido.Nome);
        Assert.Equal(esperado.Preco, obtido.Preco);
        Assert.Equal(esperado.CategoriaNome, obtido.CategoriaNome);
    }

    // 8. afterSpec — Where(p => p.Preco > 50) aplicado depois do spec REDUZ o TotalCount,
    // provando que roda antes da contagem.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComAfterSpec_ReduzTotalCountAntesDaContagem()
    {
        var request = new RequestQuery { PageSize = 10 };

        PagedResult<ProdutoDto> semAfterSpec = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        PagedResult<ProdutoDto> comAfterSpec = await _service.ApplyFilterPaginatedAsync(
            Produtos,
            request,
            ProdutoDto_Projection(),
            afterSpec: q => q.Where(p => p.Preco > 50));

        Assert.Equal(6, semAfterSpec.Meta.TotalCount);
        Assert.Equal(5, comAfterSpec.Meta.TotalCount);
        Assert.Equal(5, comAfterSpec.Items.Count);
        Assert.DoesNotContain(comAfterSpec.Items, dto => dto.Id == SqliteInMemoryFixture.CaboHdmiId);
    }

    // 9. SkipTotalCount = true => Meta.TotalCount == 0, itens da página ainda corretos.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComSkipTotalCount_NaoContaMasRetornaItens()
    {
        var request = new RequestQuery { QueryFilter = "ativo=true", PageSize = 10, SkipTotalCount = true };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(0, result.Meta.TotalCount);
        Assert.Equal(4, result.Items.Count);
    }

    // 10. ArgumentNullException para query, request e projection nulos.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComQueryNula_LancaArgumentNullException()
    {
        IQueryable<Produto> query = null!;

        ArgumentNullException ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ApplyFilterPaginatedAsync(query, new RequestQuery(), ProdutoDto_Projection()));

        Assert.Equal("query", ex.ParamName);
    }

    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComRequestNula_LancaArgumentNullException()
    {
        RequestQuery request = null!;

        ArgumentNullException ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ApplyFilterPaginatedAsync(Produtos, request, ProdutoDto_Projection()));

        Assert.Equal("request", ex.ParamName);
    }

    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComProjecaoNula_LancaArgumentNullException()
    {
        Expression<Func<Produto, ProdutoDto>> projection = null!;

        ArgumentNullException ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ApplyFilterPaginatedAsync(Produtos, new RequestQuery(), projection));

        Assert.Equal("projection", ex.ParamName);
    }

    // 11. Filtro com "in" (categoriaid__in=1,2). O parser de RequestQuery.QueryFilter separa
    // os pares por ';' quando a string contém esse caractere; caso contrário usa ','. Como o
    // valor do operador "in" também é separado por vírgula, um filtro "in" isolado precisa de
    // um ';' (ainda que sobrando, ex. no final) para forçar o separador correto — do contrário
    // "categoriaid__in=1,2" seria partido em dois pares inválidos por causa da vírgula do "in".
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComFiltroIn_FiltraPorListaDeCategorias()
    {
        var request = new RequestQuery { QueryFilter = "categoriaid__in=1,2;", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(5, result.Items.Count);
        Assert.DoesNotContain(result.Items, dto => dto.Id == SqliteInMemoryFixture.ImpressoraId);
    }

    private static Expression<Func<Produto, ProdutoDto>> ProdutoDto_Projection() =>
        produto => new ProdutoDto
        {
            Id = produto.Id,
            Nome = produto.Nome,
            Preco = produto.Preco,
            CategoriaNome = produto.Categoria.Nome
        };
}
