using System.Linq.Expressions;
using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Extensions;
using Xunit;

namespace Queryable.Tests;

/// <summary>
/// Etapa 9 (issues #2 e #3), no núcleo: propriedade de coleção em filtro/ordenação é rejeitada
/// com <see cref="ArgumentException"/> clara (não <see cref="InvalidCastException"/>), e
/// <c>For(c =&gt; c.Produtos.Count)</c> resolve e filtra/ordena; <c>Any(...)</c> continua rejeitado
/// pelo extrator. Tradução para SQL é provada na suíte EF (<c>ColecaoTests</c> de lá).
/// </summary>
public class ColecaoTests
{
    // Harness GENÉRICO (For chamado fora do construtor) em vez de uma configuração concreta para
    // Categoria: a varredura de AddQueryableConfigurationsFromAssembly, usada por outro teste da
    // suíte, instancia toda classe concreta não genérica que herde de QueryableConfiguration<>
    // e Register é last-write-wins por tipo — uma classe concreta aqui sobrescreveria a config
    // daquele teste. Definição genérica fica fora da varredura.
    private sealed class Harness<TEntity> : QueryableConfiguration<TEntity> where TEntity : class
    {
        public QueryablePropertyBuilder<TEntity> ChamarFor<TProperty>(Expression<Func<TEntity, TProperty>> selector) => For(selector);
    }

    private static List<Categoria> CriarCategorias() =>
    [
        new Categoria { Id = 1, Nome = "Papelaria", Produtos = [new Produto { Nome = "A" }, new Produto { Nome = "B" }, new Produto { Nome = "C" }] },
        new Categoria { Id = 2, Nome = "Escritorio", Produtos = [new Produto { Nome = "D" }] },
        new Categoria { Id = 3, Nome = "Vazia", Produtos = [] }
    ];

    private static ReflectionPropertyPathProvider ProviderComConfig()
    {
        var config = new Harness<Categoria>();
        config.ChamarFor(c => c.Produtos.Count).As("qtd_produtos");
        var registry = new QueryableConfigurationRegistry();
        registry.Register(config);
        return new ReflectionPropertyPathProvider(registry);
    }

    // ----- issue #2 -----

    [Fact]
    public void Filtro_EmPropriedadeDeColecao_LancaArgumentExceptionClara_NaoInvalidCast()
    {
        var builder = new FilterBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.BuildPredicate<Categoria>(new Dictionary<string, string> { ["produtos"] = "x" }));

        Assert.Contains("'produtos' é uma coleção", ex.Message);
        Assert.Contains("For(x => x.Produtos.Count)", ex.Message);
    }

    [Fact]
    public void Filtro_In_EmPropriedadeDeColecao_LancaArgumentExceptionClara()
    {
        var builder = new FilterBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.BuildPredicate<Categoria>(new Dictionary<string, string> { ["produtos__in"] = "a,b" }));

        Assert.Contains("'produtos' é uma coleção", ex.Message);
    }

    [Fact]
    public void Filtro_EmColecaoAninhada_MensagemUsaOAliasCompleto()
    {
        var builder = new FilterBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.BuildPredicate<Produto>(new Dictionary<string, string> { ["categoria.produtos"] = "x" }));

        Assert.Contains("'categoria.produtos' é uma coleção", ex.Message);
        Assert.Contains("For(x => x.Categoria.Produtos.Count)", ex.Message);
    }

    [Fact]
    public void Ordenacao_EmPropriedadeDeColecao_LancaArgumentExceptionClara()
    {
        var builder = new SortBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.ApplySort(CriarCategorias().AsQueryable(), "produtos"));

        Assert.Contains("'produtos' é uma coleção", ex.Message);
    }

    // ----- issue #3 -----

    [Fact]
    public void Config_ForProdutosCount_ResolveParaCaminhoProdutosCount()
    {
        var paths = ProviderComConfig().GetPaths<Categoria>();

        Assert.True(paths.TryGetValue("qtd_produtos", out var path));
        Assert.Equal(["Produtos", "Count"], path!.Select(p => p.Name));
    }

    [Fact]
    public void Filtro_QtdProdutosGte2_FiltraPelaContagem()
    {
        var builder = new FilterBuilder(ProviderComConfig());
        var predicate = builder.BuildPredicate<Categoria>(new Dictionary<string, string> { ["qtd_produtos__gte"] = "2" });

        List<Categoria> resultado = CriarCategorias().AsQueryable().Where(predicate).ToList();

        Assert.Equal(["Papelaria"], resultado.Select(c => c.Nome));
    }

    [Fact]
    public void Ordenacao_PorQtdProdutosDesc_OrdenaPelaContagem()
    {
        var builder = new SortBuilder(ProviderComConfig());

        List<Categoria> resultado = builder.ApplySort(CriarCategorias().AsQueryable(), "-qtd_produtos").ToList();

        Assert.Equal(["Papelaria", "Escritorio", "Vazia"], resultado.Select(c => c.Nome));
    }

    [Fact]
    public void Config_ForProdutosAny_ContinuaRejeitadoPeloExtrator()
    {
        var config = new Harness<Categoria>();

        var ex = Assert.Throws<ArgumentException>(() => config.ChamarFor(c => c.Produtos.Any(p => p.Ativo)));

        Assert.Contains("não é uma cadeia de propriedades válida", ex.Message);
    }
}
