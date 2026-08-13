using global::Microsoft.EntityFrameworkCore;
using Queryable.Builders;
using Queryable.Filtering;
using Xunit;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Camada 3 da estratégia de testes da Etapa 2 (árvore de filtro): tradução
/// <c>FilterNode -&gt; Expression -&gt; SQL</c> contra SQLite in-memory de verdade, não
/// <c>List&lt;T&gt;.AsQueryable()</c>. <see cref="FilterNodeCompilerTests"/> (suíte núcleo) já
/// prova que o compilador produz o resultado lógico correto em memória; esta suíte prova que o
/// provider relacional do EF Core consegue efetivamente traduzir a mesma árvore composta —
/// inclusive <c>OR</c> atravessando navegação (JOIN) e <c>NOT</c> — para SQL executável, sem cair
/// em avaliação no cliente (o <see cref="TestDbContext"/> está configurado para lançar em vez de
/// fazer isso silenciosamente).
/// </summary>
public class FilterNodeTranslationTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;
    private readonly FilterBuilder _builder = new();

    public FilterNodeTranslationTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    private IQueryable<Produto> Produtos => _fixture.Context.Produtos;

    [Fact]
    public async Task Or_AtravessandoNavegacao_TraduzParaSql()
    {
        // "nome = Notebook" é campo escalar da própria entidade; "categoria.nome = Perifericos"
        // atravessa a navegação Produto -> Categoria (JOIN). Combinados por OR, isso força o EF
        // Core a traduzir uma disjunção que mistura uma coluna local com uma coluna do lado
        // direito de um JOIN — o caso que List<T>.AsQueryable() nunca exercitaria de verdade.
        var filter = new FilterGroup(FilterLogic.Or,
        [
            new FilterCondition("nome", "eq", "Notebook"),
            new FilterCondition("categoria.nome", "eq", "Perifericos")
        ]);

        var predicate = _builder.BuildPredicate<Produto>(filter);

        List<Produto> resultado = await Produtos.Where(predicate).ToListAsync();

        Assert.Equal(4, resultado.Count);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.NotebookId);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.MouseId);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.TecladoId);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.MonitorId);
    }

    [Fact]
    public async Task Not_TraduzParaSql_NegandoACondicaoInterna()
    {
        var filter = new FilterNot(new FilterCondition("ativo", "eq", "true"));

        var predicate = _builder.BuildPredicate<Produto>(filter);

        List<Produto> resultado = await Produtos.Where(predicate).ToListAsync();

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.MonitorId);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.ImpressoraId);
        Assert.All(resultado, p => Assert.False(p.Ativo));
    }

    [Fact]
    public async Task Aninhamento_OrDentroDeAnd_ComNavegacao_TraduzParaSql()
    {
        // (categoria.nome = Perifericos or categoria.nome = Escritorio) and ativo = true
        var filter = new FilterGroup(FilterLogic.And,
        [
            new FilterGroup(FilterLogic.Or,
            [
                new FilterCondition("categoria.nome", "eq", "Perifericos"),
                new FilterCondition("categoria.nome", "eq", "Escritorio")
            ]),
            new FilterCondition("ativo", "eq", "true")
        ]);

        var predicate = _builder.BuildPredicate<Produto>(filter);

        List<Produto> resultado = await Produtos.Where(predicate).ToListAsync();

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.MouseId);
        Assert.Contains(resultado, p => p.Id == SqliteInMemoryFixture.TecladoId);
    }
}
