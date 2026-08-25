using System.Linq.Expressions;
using System.Reflection;
using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Extensions;
using Queryable.Interfaces;
using Xunit;

namespace Queryable.Tests;

/// <summary>
/// Cobre <c>QueryableUsage</c>/<c>FilterOnly</c>/<c>SortOnly</c>/<c>ForSort</c>: restrição de
/// aliases a filtro ou ordenação isoladamente, expressões de ordenação compostas, interação com
/// <c>OnlyMapped</c>/<c>Ignore</c>, cache por uso e os default interface methods de
/// <see cref="IPropertyPathProvider"/>.
/// </summary>
public class AliasUsageTests
{
    // Harness GENÉRICO (For/ForSort/Ignore/OnlyMapped chamados fora do construtor), pelo mesmo
    // motivo documentado em ColecaoTests: a varredura de AddQueryableConfigurationsFromAssembly
    // (usada por outro teste da suíte) instancia toda classe concreta não genérica que herde de
    // QueryableConfiguration<> e Register é last-write-wins por tipo — uma classe concreta aqui
    // sobrescreveria a config daquele teste. Definição genérica fica fora da varredura.
    private sealed class Harness<TEntity> : QueryableConfiguration<TEntity> where TEntity : class
    {
        public QueryablePropertyBuilder<TEntity> ChamarFor<TProperty>(Expression<Func<TEntity, TProperty>> selector) =>
            For(selector);

        public QueryableSortExpressionBuilder<TEntity> ChamarForSort<TKey>(Expression<Func<TEntity, TKey>> selector) =>
            ForSort(selector);

        public void ChamarIgnore<TProperty>(Expression<Func<TEntity, TProperty>> selector) =>
            Ignore(selector);

        public void ChamarOnlyMapped() => OnlyMapped();
    }

    /// <summary>Implementação mínima externa de <see cref="IPropertyPathProvider"/>, só com <c>GetPaths&lt;T&gt;()</c> — prova dos DIMs (caso 14).</summary>
    private sealed class ProviderMinimo : IPropertyPathProvider
    {
        public IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths<T>()
        {
            var caminho = new List<PropertyInfo> { typeof(Produto).GetProperty(nameof(Produto.Nome))! };
            return new Dictionary<string, List<PropertyInfo>>(StringComparer.OrdinalIgnoreCase)
            {
                ["nome"] = caminho
            };
        }
    }

    private static IPropertyPathProvider CriarProvider<TEntity>(QueryableConfiguration<TEntity> configuracao)
        where TEntity : class
    {
        var registry = new QueryableConfigurationRegistry();
        registry.Register(configuracao);
        return new ReflectionPropertyPathProvider(registry);
    }

    private static List<Produto> CriarProdutos() =>
    [
        new() { Nome = "Caneta", NomeSocial = null, Preco = 10m },
        new() { Nome = "Bola", NomeSocial = "Zeta", Preco = 5m },
        new() { Nome = "Abacate", NomeSocial = "Alfa", Preco = 20m }
    ];

    // ----- casos 1 e 2: FilterOnly / SortOnly aparecem só no mapa correspondente -----

    [Fact]
    public void FilterOnly_ApareceApenasNoMapaDeFiltro()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Nome).As("nomeFiltro").FilterOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("nomefiltro"));
        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("nomefiltro"));
        Assert.False(provider.GetPaths<Produto>().ContainsKey("nomefiltro"));
    }

    [Fact]
    public void SortOnly_ApareceApenasNoMapaDeOrdenacao()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Nome).As("nomeSort").SortOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("nomesort"));
        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("nomesort"));
        Assert.False(provider.GetPaths<Produto>().ContainsKey("nomesort"));
    }

    // ----- caso 3: alias sem qualificador aparece nos três mapas -----

    [Fact]
    public void AliasSemQualificador_ApareceNosTresMapas()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Nome).As("nomePadrao");
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("nomepadrao"));
        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("nomepadrao"));
        Assert.True(provider.GetPaths<Produto>().ContainsKey("nomepadrao"));
    }

    // ----- caso 4: As depois de FilterOnly preserva uso; last-write-wins -----

    [Fact]
    public void As_DepoisDeFilterOnly_PreservaUso()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Nome).FilterOnly().As("renomeado");
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("renomeado"));
        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("renomeado"));
    }

    [Fact]
    public void FilterOnlyDepoisSortOnly_LastWriteWins_FicaSort()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Nome).As("alias").FilterOnly().SortOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("alias"));
        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("alias"));
    }

    // ----- caso 5: FilterBuilder com alias SortOnly -> "não é pesquisável" -----

    [Fact]
    public void FilterBuilder_ComAliasSortOnly_LancaNaoEhPesquisavel()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Nome).As("soOrdenacao").SortOnly();
        var filterBuilder = new FilterBuilder(CriarProvider(config));

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            filterBuilder.BuildPredicate<Produto>(new Dictionary<string, string> { ["soOrdenacao"] = "x" }));

        Assert.Contains("não é pesquisável", ex.Message);
    }

    // ----- caso 6: SortBuilder com alias FilterOnly -> "não encontrado" (sem "filtro") -----

    [Fact]
    public void SortBuilder_ComAliasFilterOnly_LancaNaoEncontrado()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Nome).As("campoRestrito").FilterOnly();
        var sortBuilder = new SortBuilder(CriarProvider(config));

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            sortBuilder.ApplySort(CriarProdutos().AsQueryable(), "campoRestrito"));

        Assert.Contains("não encontrado", ex.Message);
        Assert.DoesNotContain("filtro", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ----- caso 7: ForSort com expressão composta -----

    [Fact]
    public void ForSort_ExpressaoComposta_ApareceEmGetSortExpressions()
    {
        var config = new Harness<Produto>();
        config.ChamarForSort(p => p.NomeSocial ?? p.Nome).As("nomeExibicao");
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.True(provider.GetSortExpressions<Produto>().ContainsKey("nomeexibicao"));
    }

    [Fact]
    public void ForSort_ExpressaoComposta_OrdenaPeloCoalesceAscDesc()
    {
        var config = new Harness<Produto>();
        config.ChamarForSort(p => p.NomeSocial ?? p.Nome).As("nomeExibicao");
        var sortBuilder = new SortBuilder(CriarProvider(config));

        // NomeSocial ?? Nome: Caneta -> "Caneta"; Bola -> "Zeta"; Abacate -> "Alfa".
        var asc = sortBuilder.ApplySort(CriarProdutos().AsQueryable(), "nomeExibicao").ToList();
        Assert.Equal(["Abacate", "Caneta", "Bola"], asc.Select(p => p.Nome));

        var desc = sortBuilder.ApplySort(CriarProdutos().AsQueryable(), "-nomeExibicao").ToList();
        Assert.Equal(["Bola", "Caneta", "Abacate"], desc.Select(p => p.Nome));
    }

    [Fact]
    public void ForSort_ExpressaoComposta_CombinaComThenBy()
    {
        var config = new Harness<Produto>();
        config.ChamarForSort(p => p.NomeSocial ?? p.Nome).As("nomeExibicao");
        var sortBuilder = new SortBuilder(CriarProvider(config));

        var produtos = new List<Produto>
        {
            new() { Nome = "Caneta", NomeSocial = "Alfa", Preco = 30m },
            new() { Nome = "Bola", NomeSocial = "Alfa", Preco = 10m },
            new() { Nome = "Abacate", NomeSocial = null, Preco = 20m }
        };

        var resultado = sortBuilder.ApplySort(produtos.AsQueryable(), "nomeExibicao,-valor").ToList();

        // nomeExibicao asc: "Abacate" (Abacate, sem NomeSocial) < "Alfa" (Caneta) == "Alfa" (Bola);
        // empate entre Caneta/Bola desempatado por -valor (alias automático de Preco, ver
        // [Queryable("valor")] em TestModels.cs): Caneta (30) antes de Bola (10).
        Assert.Equal(["Abacate", "Caneta", "Bola"], resultado.Select(p => p.Nome));
    }

    // ----- caso 8: ForSort alias em FilterBuilder -> "não é pesquisável" -----

    [Fact]
    public void ForSort_AliasEmFilterBuilder_LancaNaoEhPesquisavel()
    {
        var config = new Harness<Produto>();
        config.ChamarForSort(p => p.NomeSocial ?? p.Nome).As("nomeExibicao");
        var filterBuilder = new FilterBuilder(CriarProvider(config));

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            filterBuilder.BuildPredicate<Produto>(new Dictionary<string, string> { ["nomeExibicao"] = "Alfa" }));

        Assert.Contains("não é pesquisável", ex.Message);
    }

    // ----- caso 9: ForSort sem As -> Register lança InvalidOperationException -----

    [Fact]
    public void ForSort_SemAs_RegisterLancaInvalidOperationException()
    {
        var config = new Harness<Produto>();
        config.ChamarForSort(p => p.NomeSocial ?? p.Nome);

        var registry = new QueryableConfigurationRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(config));
    }

    // ----- caso 10: ForSort não lança na configuração; For com mesma expressão continua lançando -----

    [Fact]
    public void ForSort_ComExpressaoComposta_NaoLancaNaConfiguracao()
    {
        var config = new Harness<Produto>();

        QueryableSortExpressionBuilder<Produto> builder = config.ChamarForSort(p => p.NomeSocial ?? p.Nome);
        builder.As("nomeExibicao");
        // Não deve lançar até aqui.
    }

    [Fact]
    public void For_ComExpressaoComposta_ContinuaLancandoArgumentException()
    {
        var config = new Harness<Produto>();

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            config.ChamarFor(p => p.NomeSocial ?? p.Nome));

        Assert.Contains("não é uma cadeia de propriedades válida", ex.Message);
    }

    // ----- caso 11: OnlyMapped + FilterOnly/SortOnly em campos diferentes -----

    [Fact]
    public void OnlyMapped_ComFilterOnlyESortOnlyEmCamposDiferentes_SeparaOsMapas()
    {
        var config = new Harness<Produto>();
        config.ChamarOnlyMapped();
        config.ChamarFor(p => p.Nome).As("nome").FilterOnly();
        config.ChamarFor(p => p.Preco).As("preco").SortOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        var mapaFilter = provider.GetPaths<Produto>(QueryableUsage.Filter);
        var mapaSort = provider.GetPaths<Produto>(QueryableUsage.Sort);

        Assert.Equal(["nome"], mapaFilter.Keys);
        Assert.Equal(["preco"], mapaSort.Keys);
    }

    // ----- caso 12: Ignore remove dos mapas Filter e Sort -----

    [Fact]
    public void Ignore_RemoveDosMapasFilterESort()
    {
        var config = new Harness<Produto>();
        config.ChamarIgnore(p => p.Peso);
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("peso"));
        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("peso"));
    }

    // ----- caso 13: cache por usage -----

    [Fact]
    public void GetPaths_MesmoUsage_DevolveMesmaInstancia_UsagesDiferentesDevolveminstanciasDiferentes()
    {
        var provider = new ReflectionPropertyPathProvider();

        var filtro1 = provider.GetPaths<Produto>(QueryableUsage.Filter);
        var filtro2 = provider.GetPaths<Produto>(QueryableUsage.Filter);
        var sort1 = provider.GetPaths<Produto>(QueryableUsage.Sort);

        Assert.Same(filtro1, filtro2);
        Assert.NotSame(filtro1, sort1);
    }

    // ----- caso 14: implementação externa mínima de IPropertyPathProvider (DIMs) -----

    [Fact]
    public void ImplementacaoExternaMinima_FuncionaComFilterBuilderESortBuilder()
    {
        var provider = new ProviderMinimo();
        var filterBuilder = new FilterBuilder(provider);
        var sortBuilder = new SortBuilder(provider);

        var produtos = new List<Produto>
        {
            new() { Nome = "Zeta" },
            new() { Nome = "Alfa" }
        };

        var predicate = filterBuilder.BuildPredicate<Produto>(new Dictionary<string, string> { ["nome"] = "Alfa" });
        List<Produto> filtrados = produtos.AsQueryable().Where(predicate).ToList();
        Assert.Single(filtrados);
        Assert.Equal("Alfa", filtrados[0].Nome);

        var ordenados = sortBuilder.ApplySort(produtos.AsQueryable(), "nome").ToList();
        Assert.Equal(["Alfa", "Zeta"], ordenados.Select(p => p.Nome));
    }

    // ----- caso 15: FilterOnly sem As remove o caminho automático padrão do mapa de Sort -----

    [Fact]
    public void FilterOnly_SemAs_RemoveAliasAutomaticoDoMapaDeSort()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Ativo).FilterOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("ativo"));
        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("ativo"));

        var sortBuilder = new SortBuilder(provider);
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            sortBuilder.ApplySort(CriarProdutos().AsQueryable(), "ativo"));

        Assert.Contains("não encontrado", ex.Message);
    }

    // ----- caso 16: SortOnly sem As remove o alias automático de [Queryable("...")] do mapa de Filter -----

    [Fact]
    public void SortOnly_SemAs_RemoveAliasAutomaticoDoAtributoDoMapaDeFiltro()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Preco).SortOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        var mapaFilter = provider.GetPaths<Produto>(QueryableUsage.Filter);
        Assert.False(mapaFilter.ContainsKey("preco"));
        Assert.False(mapaFilter.ContainsKey("valor"));
        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("preco"));

        var filterBuilder = new FilterBuilder(provider);
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            filterBuilder.BuildPredicate<Produto>(new Dictionary<string, string> { ["valor"] = "10" }));

        Assert.Contains("não é pesquisável", ex.Message);
    }

    // ----- caso 17: SortOnly sem As em caminho aninhado remove o alias automático aninhado do mapa de Filter -----

    [Fact]
    public void SortOnly_SemAs_EmCaminhoAninhado_RemoveAliasAutomaticoDoMapaDeFiltro()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Categoria.Nome).SortOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.False(provider.GetPaths<Produto>(QueryableUsage.Filter).ContainsKey("categoria.nome"));
        Assert.True(provider.GetPaths<Produto>(QueryableUsage.Sort).ContainsKey("categoria.nome"));
    }

    // ----- caso 18: caminho automático de alias FilterOnly também sai do mapa "All" -----

    [Fact]
    public void FilterOnly_SemAs_TambemRemoveAliasAutomaticoDoMapaAll()
    {
        var config = new Harness<Produto>();
        config.ChamarFor(p => p.Ativo).FilterOnly();
        IPropertyPathProvider provider = CriarProvider(config);

        Assert.False(provider.GetPaths<Produto>().ContainsKey("ativo"));
    }
}
