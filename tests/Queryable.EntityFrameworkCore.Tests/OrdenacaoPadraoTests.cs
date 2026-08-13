using System.Linq.Expressions;
using Queryable.Builders;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.EntityFrameworkCore.Services;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Prova, contra SQLite in-memory, a armadilha de ordenação documentada em
/// <see cref="IPagedQueryService.ApplyFilterPaginatedAsync{TEntity, TDto}(IQueryable{TEntity}, RequestQuery, Expression{Func{TEntity, TDto}}, Func{IQueryable{TEntity}, IQueryable{TEntity}}?, CancellationToken)"/>:
/// a ordenação interna de <c>QuerySpecApplier.Apply</c> usa sempre <c>OrderBy</c> (nunca
/// <c>ThenBy</c>), inclusive no fallback sem <c>sort</c> (<c>OrderBy(x =&gt; 0)</c>), e por isso
/// descarta qualquer ordenação anterior da <see cref="IQueryable{T}"/> — tanto a que o chamador
/// aplicou em <c>query</c> antes de passá-la, quanto a que o cliente pediu via
/// <see cref="RequestQuery.Sort"/>, se um <c>afterSpec</c> reordenar incondicionalmente.
/// </summary>
public class OrdenacaoPadraoTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;
    private readonly IPagedQueryService _service;

    public OrdenacaoPadraoTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
        _service = new PagedQueryService(new QuerySpecApplier(new FilterBuilder(), new SortBuilder()));
    }

    private IQueryable<Produto> Produtos => _fixture.Context.Produtos;

    // Ordem que "-preco" produz explicitamente (mesma sequência do teste 4 de
    // PagedQueryServiceTests) — usada só como sequência de referência para provar que o
    // resultado de um caminho diferente NÃO é esta.
    private static readonly Guid[] OrdemExplicitaPorPrecoDesc =
    [
        SqliteInMemoryFixture.NotebookId,
        SqliteInMemoryFixture.MonitorId,
        SqliteInMemoryFixture.ImpressoraId,
        SqliteInMemoryFixture.TecladoId,
        SqliteInMemoryFixture.MouseId,
        SqliteInMemoryFixture.CaboHdmiId
    ];

    // 1. Ordenação prévia da query é descartada: query.OrderByDescending(p => p.Preco) passada
    // como `query`, sem Sort no request, produz o MESMO resultado que passar a query sem
    // nenhuma ordenação prévia — porque os dois caem no mesmo fallback OrderBy(x => 0) de
    // QuerySpecApplier.Apply, que ignora a ordenação de entrada. Comprovado empiricamente: com
    // este dataset, SQLite devolve a ordem de inserção (rowid) para ambos os casos, que é
    // estável e diferente da ordem explícita por preço — por isso a asserção compara contra o
    // baseline (execução equivalente sem pré-ordenação) em vez de fixar a ordem de inserção como
    // constante da lib, o que seria um detalhe de implementação do SQLite e não do comportamento
    // sob teste.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComOrdenacaoPreviaNaQuery_DescartaAOrdenacaoPrevia()
    {
        var request = new RequestQuery { PageSize = 10 };

        PagedResult<ProdutoDto> baseline = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        PagedResult<ProdutoDto> comOrdenacaoPrevia = await _service.ApplyFilterPaginatedAsync(
            Produtos.OrderByDescending(p => p.Preco), request, ProdutoDto_Projection());

        Guid[] ordemBaseline = baseline.Items.Select(i => i.Id).ToArray();
        Guid[] ordemComOrdenacaoPrevia = comOrdenacaoPrevia.Items.Select(i => i.Id).ToArray();

        // A ordenação prévia (por preço desc) foi descartada: o resultado é idêntico ao
        // baseline sem nenhuma ordenação de entrada...
        Assert.Equal(ordemBaseline, ordemComOrdenacaoPrevia);

        // ...e, portanto, diferente da ordem que "-preco" produziria de fato (prova de que a
        // ordenação prévia não sobreviveu por baixo dos panos).
        Assert.NotEqual(OrdemExplicitaPorPrecoDesc, ordemComOrdenacaoPrevia);
    }

    // 2. afterSpec ordena com sucesso: sem Sort no request, um OrderByDescending dentro de
    // afterSpec produz a ordem pedida — prova de que a ordenação padrão tem que morar ali.
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComOrdenacaoNoAfterSpec_OrdenaPorPreco()
    {
        var request = new RequestQuery { PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos,
            request,
            ProdutoDto_Projection(),
            afterSpec: q => q.OrderByDescending(p => p.Preco));

        Assert.Equal(OrdemExplicitaPorPrecoDesc, result.Items.Select(i => i.Id));
    }

    // 3. afterSpec incondicional sobrescreve o sort do cliente: Sort = "nome" no request E um
    // OrderByDescending(Preco) incondicional em afterSpec — o resultado sai ordenado por preço,
    // não por nome, porque afterSpec roda depois de Apply e seu OrderBy também descarta a
    // ordenação anterior (a que Apply acabou de aplicar a partir de Sort).
    [Fact]
    public async Task ApplyFilterPaginatedAsync_ComAfterSpecIncondicional_SobrescreveOSortDoCliente()
    {
        var request = new RequestQuery { Sort = "nome", PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos,
            request,
            ProdutoDto_Projection(),
            afterSpec: q => q.OrderByDescending(p => p.Preco));

        // Sai ordenado por preço (o do afterSpec)...
        Assert.Equal(OrdemExplicitaPorPrecoDesc, result.Items.Select(i => i.Id));

        // ...e não pela ordem alfabética por nome que Sort = "nome" pediria de fato.
        Guid[] ordemPorNomeQueOSortPediu = Produtos
            .OrderBy(p => p.Nome)
            .Select(p => p.Id)
            .ToArray();

        Assert.NotEqual(ordemPorNomeQueOSortPediu, result.Items.Select(i => i.Id));
    }

    // 4. Padrão condicional funciona: afterSpec só ordena por preço quando request.Sort está
    // vazio. Com Sort = "nome" preenchido, o pedido do cliente é respeitado; sem Sort, o padrão
    // (preço desc) entra em cena.
    [Theory]
    [InlineData(null)]
    [InlineData("nome")]
    public async Task ApplyFilterPaginatedAsync_ComOrdenacaoPadraoCondicional_RespeitaOSortQuandoPresente(string? sort)
    {
        var request = new RequestQuery { Sort = sort, PageSize = 10 };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos,
            request,
            ProdutoDto_Projection(),
            afterSpec: q => string.IsNullOrWhiteSpace(request.Sort)
                ? q.OrderByDescending(p => p.Preco)
                : q);

        if (sort is null)
        {
            // Sem Sort do cliente: entra o padrão do afterSpec (preço desc).
            Assert.Equal(OrdemExplicitaPorPrecoDesc, result.Items.Select(i => i.Id));
        }
        else
        {
            // Com Sort = "nome" do cliente: o afterSpec não mexe, e a ordem é por nome (a
            // que Apply já produziu a partir de request.Sort).
            Guid[] esperado = Produtos
                .OrderBy(p => p.Nome)
                .Select(p => p.Id)
                .ToArray();

            Assert.Equal(esperado, result.Items.Select(i => i.Id));
        }
    }

    // 5. afterSpec afeta o TotalCount: já coberto por
    // PagedQueryServiceTests.ApplyFilterPaginatedAsync_ComAfterSpec_ReduzTotalCountAntesDaContagem
    // (Where(p => p.Preco > 50) em afterSpec reduz Meta.TotalCount de 6 para 5) — não duplicado
    // aqui.

    private static Expression<Func<Produto, ProdutoDto>> ProdutoDto_Projection() =>
        produto => new ProdutoDto
        {
            Id = produto.Id,
            Nome = produto.Nome,
            Preco = produto.Preco,
            CategoriaNome = produto.Categoria.Nome
        };
}
