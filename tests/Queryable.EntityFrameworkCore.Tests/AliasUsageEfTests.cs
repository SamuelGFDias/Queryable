using System.Linq.Expressions;
using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.EntityFrameworkCore.Services;
using Queryable.Extensions;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Contra SQLite in-memory: prova que <c>ForSort(p =&gt; p.NomeSocial ?? p.Nome).As("nomeExibicao")</c>
/// (expressão de ordenação composta, ver <c>QueryableSortExpressionBuilder</c>) traduz o
/// coalesce para SQL — LINQ to Objects mascararia falha de tradução, por isso esta suíte, não a
/// núcleo. Cobre também <c>For(...).FilterOnly()</c>/<c>SortOnly()</c> (uso restrito por alias).
/// </summary>
public class AliasUsageEfTests : IClassFixture<SqliteInMemoryFixture>
{
    private sealed class ProdutoUsageConfig : QueryableConfiguration<Produto>
    {
        public ProdutoUsageConfig()
        {
            ForSort(p => p.NomeSocial ?? p.Nome).As("nomeExibicao");
            For(p => p.Preco).SortOnly();
            For(p => p.Ativo).FilterOnly();
        }
    }

    private readonly SqliteInMemoryFixture _fixture;
    private readonly IPagedQueryService _service;

    public AliasUsageEfTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
        var registry = new QueryableConfigurationRegistry();
        registry.Register(new ProdutoUsageConfig());
        var provider = new ReflectionPropertyPathProvider(registry);
        _service = new PagedQueryService(new QuerySpecApplier(new FilterBuilder(provider), new SortBuilder(provider)));
    }

    private IQueryable<Produto> Produtos => _fixture.Context.Produtos;

    // Ordem esperada por coalesce(NomeSocial, Nome) asc: Notebook (NomeSocial "Aaa Note"),
    // Impressora, Monitor 24 Polegadas, Mouse Gamer, Teclado Mecanico, Cabo HDMI (NomeSocial
    // "Zzz Cabo") — difere propositalmente da ordem por Nome puro (que começaria em "Cabo HDMI").
    private static readonly Guid[] OrdemPorNomeExibicaoAsc =
    [
        SqliteInMemoryFixture.NotebookId,
        SqliteInMemoryFixture.ImpressoraId,
        SqliteInMemoryFixture.MonitorId,
        SqliteInMemoryFixture.MouseId,
        SqliteInMemoryFixture.TecladoId,
        SqliteInMemoryFixture.CaboHdmiId
    ];

    // 1. Sort = "nomeExibicao" traduz o coalesce para SQL e ordena pelo resultado (asc); a
    // sequência resultante difere da ordem por Nome puro.
    [Fact]
    public async Task Sort_NomeExibicaoAsc_TraduzOCoalesceParaSqlEDifereDaOrdemPorNome()
    {
        var request = new RequestQuery { Sort = "nomeExibicao", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(OrdemPorNomeExibicaoAsc, result.Items.Select(i => i.Id));

        Guid[] ordemPorNomePuro = Produtos.OrderBy(p => p.Nome).Select(p => p.Id).ToArray();
        Assert.NotEqual(ordemPorNomePuro, result.Items.Select(i => i.Id));
    }

    // 2. Sort = "-nomeExibicao" inverte a ordem do coalesce.
    [Fact]
    public async Task Sort_NomeExibicaoDesc_InverteAOrdemDoCoalesce()
    {
        var request = new RequestQuery { Sort = "-nomeExibicao", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(OrdemPorNomeExibicaoAsc.Reverse(), result.Items.Select(i => i.Id));
    }

    // 3. Sort = "nomeExibicao,-preco" combina ThenBy (alias normal, resolvido por
    // Expression.Property) com a expressão composta de ForSort — os dois traduzem juntos.
    [Fact]
    public async Task Sort_NomeExibicaoComThenByPreco_TraduzOsDoisCaminhosJuntos()
    {
        var request = new RequestQuery { Sort = "nomeExibicao,-preco", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        // Valores de nomeExibicao são todos distintos neste dataset, então o ThenBy por
        // -preco não muda a ordem primária — a asserção prova que a consulta não lança
        // (traduz com sucesso) e produz a mesma ordem primária do teste 1.
        Assert.Equal(OrdemPorNomeExibicaoAsc, result.Items.Select(i => i.Id));
    }

    // 4. QueryFilter em "nomeexibicao" (alias existe só para ordenação, via ForSort) lança
    // ArgumentException "não é pesquisável".
    [Fact]
    public async Task Filtro_PorNomeExibicao_LancaArgumentExceptionNaoEPesquisavel()
    {
        var request = new RequestQuery { QueryFilter = "nomeexibicao=x" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ApplyFilterPaginatedAsync(Produtos, request, ProdutoDto_Projection()));

        Assert.Contains("não é pesquisável", ex.Message);
    }

    // 5. Sort = "ativo" (FilterOnly) lança ArgumentException "não encontrado"; QueryFilter =
    // "ativo=true" (uso permitido) funciona normalmente e retorna os 4 produtos ativos.
    [Fact]
    public async Task Ordenacao_PorAtivoFilterOnly_LancaArgumentExceptionNaoEncontrado()
    {
        var request = new RequestQuery { Sort = "ativo" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ApplyFilterPaginatedAsync(Produtos, request, ProdutoDto_Projection()));

        Assert.Contains("não encontrado", ex.Message);
    }

    [Fact]
    public async Task Filtro_PorAtivoFilterOnly_Funciona()
    {
        var request = new RequestQuery { QueryFilter = "ativo=true", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(4, result.Items.Count);
    }

    // 6. QueryFilter em "preco" (SortOnly) lança ArgumentException "não é pesquisável"; Sort =
    // "-preco" (uso permitido) funciona normalmente.
    [Fact]
    public async Task Filtro_PorPrecoSortOnly_LancaArgumentExceptionNaoEPesquisavel()
    {
        var request = new RequestQuery { QueryFilter = "preco__gt=100" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ApplyFilterPaginatedAsync(Produtos, request, ProdutoDto_Projection()));

        Assert.Contains("não é pesquisável", ex.Message);
    }

    [Fact]
    public async Task Ordenacao_PorPrecoSortOnly_Funciona()
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

    // 7. Sanidade: ApplyFilterPaginatedAsync com Sort = "nomeExibicao" e uma projeção que inclui
    // NomeSocial não lança e Meta.TotalCount == 6.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComProjecaoIncluindoNomeSocial_NaoLancaETotalCountEh6()
    {
        var request = new RequestQuery { Sort = "nomeExibicao", PageSize = 10 };

        Expression<Func<Produto, ProdutoComNomeSocialDto>> projecao = p => new ProdutoComNomeSocialDto
        {
            Id = p.Id,
            Nome = p.Nome,
            NomeSocial = p.NomeSocial
        };

        PagedResult<ProdutoComNomeSocialDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, projecao);

        Assert.Equal(6, result.Meta.TotalCount);
    }

    private static Expression<Func<Produto, ProdutoDto>> ProdutoDto_Projection() =>
        produto => new ProdutoDto
        {
            Id = produto.Id,
            Nome = produto.Nome,
            Preco = produto.Preco,
            CategoriaNome = produto.Categoria.Nome
        };

    private sealed class ProdutoComNomeSocialDto
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? NomeSocial { get; set; }
    }
}
