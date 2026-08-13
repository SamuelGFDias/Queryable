using System.Text.Json;
using global::Microsoft.EntityFrameworkCore;
using Queryable.Builders;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.EntityFrameworkCore.Services;
using Queryable.Filtering;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Prova, ponta a ponta e contra SQLite in-memory de verdade, que um filtro composto vindo de
/// JSON (a porta de entrada da Etapa 4, <see cref="RequestQuery.Filter"/>) é traduzido para SQL
/// pelo EF Core quando aplicado via <see cref="IPagedQueryService"/>. <see cref="FilterNodeTranslationTests"/>
/// já prova que o compilador <c>FilterNode -&gt; Expression</c> traduz corretamente uma árvore
/// montada em código; esta suíte prova o caminho completo <c>JSON -&gt; FilterNode -&gt;
/// RequestQuery -&gt; QuerySpec -&gt; SQL</c>, que é o caminho novo (OR/agrupamento/NOT eram
/// impossíveis de expressar via <see cref="RequestQuery.QueryFilter"/> antes da Etapa 4).
/// <see cref="TestDbContext"/> está configurado para lançar em vez de cair silenciosamente em
/// avaliação no cliente, então basta nunca chamar <c>AsEnumerable()</c>/<c>ToList()</c> antes de
/// <c>await</c>.
/// </summary>
public class FilterJsonEndToEndTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;
    private readonly IPagedQueryService _service;

    public FilterJsonEndToEndTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
        _service = new PagedQueryService(new QuerySpecApplier(new FilterBuilder(), new SortBuilder()));
    }

    private IQueryable<Produto> Produtos => _fixture.Context.Produtos;

    // 1. JSON -> árvore -> SQL: OR sobre "nome" (campo escalar). OR era impossível de expressar
    // via RequestQuery.QueryFilter antes da Etapa 4 — este é o caso central que prova a porta
    // JSON funcionando de ponta a ponta contra o provider relacional.
    [Fact]
    public async Task Filter_ComGrupoOrViaJson_TraduzParaSqlERetornaUniaoDosDoisConjuntos()
    {
        const string json = """
        {
            "logic": "or",
            "children": [
                { "field": "nome", "value": "Mouse Gamer" },
                { "field": "nome", "value": "Notebook" }
            ]
        }
        """;

        var request = new RequestQuery
        {
            Filter = JsonSerializer.Deserialize<FilterNode>(json),
            PageSize = 10
        };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.MouseId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.NotebookId);
    }

    // 2. Combinação dicionário + árvore: QueryFilter ("ativo=true") AND Filter (grupo OR sobre
    // categoria.nome) — o resultado deve ser a interseção dos dois conjuntos.
    [Fact]
    public async Task Filter_CombinadoComQueryFilter_AplicaAndEntreOsDoisConjuntos()
    {
        const string json = """
        {
            "logic": "or",
            "children": [
                { "field": "categoria.nome", "value": "Perifericos" },
                { "field": "categoria.nome", "value": "Informatica" }
            ]
        }
        """;

        var request = new RequestQuery
        {
            QueryFilter = "ativo=true",
            Filter = JsonSerializer.Deserialize<FilterNode>(json),
            PageSize = 10
        };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        // Perifericos ou Informatica: Mouse, Teclado, Monitor, Notebook, CaboHdmi.
        // Interseção com ativo=true exclui o Monitor (inativo).
        Assert.Equal(4, result.Items.Count);
        Assert.DoesNotContain(result.Items, dto => dto.Id == SqliteInMemoryFixture.MonitorId);
        Assert.DoesNotContain(result.Items, dto => dto.Id == SqliteInMemoryFixture.ImpressoraId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.MouseId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.TecladoId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.NotebookId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.CaboHdmiId);
    }

    // 3. "not" vindo de JSON, aplicado contra o banco.
    [Fact]
    public async Task Filter_ComNotViaJson_NegaACondicaoInternaContraOBanco()
    {
        const string json = """{ "not": { "field": "ativo", "value": "true" } }""";

        var request = new RequestQuery
        {
            Filter = JsonSerializer.Deserialize<FilterNode>(json),
            PageSize = 10
        };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.MonitorId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.ImpressoraId);
    }

    // 4. Grupo aninhado (or contendo and) vindo de JSON:
    // (categoria.nome = Perifericos and ativo = true) or categoria.nome = Escritorio
    [Fact]
    public async Task Filter_ComGrupoAninhadoViaJson_TraduzParaSql()
    {
        const string json = """
        {
            "logic": "or",
            "children": [
                {
                    "logic": "and",
                    "children": [
                        { "field": "categoria.nome", "value": "Perifericos" },
                        { "field": "ativo", "value": "true" }
                    ]
                },
                { "field": "categoria.nome", "value": "Escritorio" }
            ]
        }
        """;

        var request = new RequestQuery
        {
            Filter = JsonSerializer.Deserialize<FilterNode>(json),
            PageSize = 10
        };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.MouseId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.TecladoId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.ImpressoraId);
    }

    // 5. OR atravessando navegação, pela porta JSON: condições sobre "nome" (escalar) e
    // "categoria.nome" (via JOIN) combinadas por or. FilterNodeTranslationTests já cobre este
    // mesmo cenário lógico com a árvore montada em código; aqui o objetivo é provar que o mesmo
    // JOIN é traduzido quando a árvore chega desserializada de JSON, o caminho novo da Etapa 4.
    [Fact]
    public async Task Filter_ComOrAtravessandoNavegacaoViaJson_TraduzJoinParaSql()
    {
        const string json = """
        {
            "logic": "or",
            "children": [
                { "field": "nome", "value": "Notebook" },
                { "field": "categoria.nome", "value": "Perifericos" }
            ]
        }
        """;

        var request = new RequestQuery
        {
            Filter = JsonSerializer.Deserialize<FilterNode>(json),
            PageSize = 10
        };

        PagedResult<ProdutoDto> result = await _service.ApplyFilterPaginatedAsync(
            Produtos, request, ProdutoDto_Projection());

        Assert.Equal(4, result.Items.Count);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.NotebookId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.MouseId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.TecladoId);
        Assert.Contains(result.Items, dto => dto.Id == SqliteInMemoryFixture.MonitorId);
    }

    // 6. Filter nulo => comportamento idêntico ao de antes da Etapa 4 (regressão): só
    // QueryFilter é considerado.
    [Fact]
    public async Task Filter_Nulo_ComportaComoAntesDaEtapa4()
    {
        var request = new RequestQuery { QueryFilter = "ativo=true", Filter = null, PageSize = 10 };

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

    private static System.Linq.Expressions.Expression<Func<Produto, ProdutoDto>> ProdutoDto_Projection() =>
        produto => new ProdutoDto
        {
            Id = produto.Id,
            Nome = produto.Nome,
            Preco = produto.Preco,
            CategoriaNome = produto.Categoria.Nome
        };
}
