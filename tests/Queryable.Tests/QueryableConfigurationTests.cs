using System.Linq.Expressions;
using Xunit;
using Queryable.Builders;
using Queryable.Configuration;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.Tests;

public class QueryableConfigurationTests
{
    // ----- configurações usadas pelos testes deste arquivo -----

    private sealed class ProdutoConfigColapsaCategoria : QueryableConfiguration<Produto>
    {
        public ProdutoConfigColapsaCategoria()
        {
            For(p => p.Categoria.Nome).As("categoria");
        }
    }

    private sealed class ProdutoConfigIgnoraPeso : QueryableConfiguration<Produto>
    {
        public ProdutoConfigIgnoraPeso()
        {
            Ignore(p => p.Peso);
        }
    }

    private sealed class ProdutoConfigOnlyMapped : QueryableConfiguration<Produto>
    {
        public ProdutoConfigOnlyMapped()
        {
            OnlyMapped();
            For(p => p.Nome).As("nome");
            For(p => p.Categoria.Nome).As("categoria");
            // NumeroOpcional representa aqui um campo sensível: não é mapeado, então some do
            // mapa final assim que OnlyMapped() é chamado para Produto.
        }
    }

    private sealed class ProdutoConfigAliasCaseVariado : QueryableConfiguration<Produto>
    {
        public ProdutoConfigAliasCaseVariado()
        {
            For(p => p.Categoria.Nome).As("CaTeGoRiA");
        }
    }

    // Harness só para exercitar For/Ignore fora do construtor (evita lançar exceção durante um
    // Activator.CreateInstance de varredura de assembly em outro teste da suíte).
    private sealed class HarnessProdutoQueryConfiguration : QueryableConfiguration<Produto>
    {
        public QueryablePropertyBuilder<Produto> ChamarFor<TProperty>(Expression<Func<Produto, TProperty>> selector) =>
            For(selector);

        public void ChamarIgnore<TProperty>(Expression<Func<Produto, TProperty>> selector) =>
            Ignore(selector);
    }

    private sealed class HarnessComCampoQueryConfiguration : QueryableConfiguration<ComCampoPublico>
    {
        public QueryablePropertyBuilder<ComCampoPublico> ChamarFor<TProperty>(Expression<Func<ComCampoPublico, TProperty>> selector) =>
            For(selector);
    }

    private static IPropertyPathProvider CriarProvider<TEntity>(QueryableConfiguration<TEntity> configuracao)
        where TEntity : class
    {
        var registry = new QueryableConfigurationRegistry();
        registry.Register(configuracao);
        return new ReflectionPropertyPathProvider(registry);
    }

    // ----- colapso de value object / caminho aninhado -----

    [Fact]
    public void AliasConfigurado_ColapsaValueObject_ResolveParaCaminhoAninhado()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigColapsaCategoria());

        IReadOnlyDictionary<string, List<System.Reflection.PropertyInfo>> caminhos = provider.GetPaths<Produto>();

        Assert.True(caminhos.TryGetValue("categoria", out List<System.Reflection.PropertyInfo>? caminho));
        Assert.Equal(new[] { "Categoria", "Nome" }, caminho!.Select(p => p.Name).ToArray());
    }

    // ----- coexistência entre alias configurado e alias automático antigo (decisão 2) -----

    [Fact]
    public void AliasConfigurado_Coexiste_ComAliasAutomaticoAntigo()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigColapsaCategoria());

        var caminhos = provider.GetPaths<Produto>();

        Assert.True(caminhos.ContainsKey("categoria"));
        Assert.True(caminhos.ContainsKey("categoria.nome"));
        Assert.Equal(caminhos["categoria"], caminhos["categoria.nome"]);
    }

    // ----- sobrescrita do automático de mesmo alias -----

    [Fact]
    public void AliasConfigurado_SobrescreveEntradaAutomaticaDeMesmoAlias()
    {
        var providerSemConfig = new ReflectionPropertyPathProvider();
        var automatico = providerSemConfig.GetPaths<Produto>();
        Assert.Equal(new[] { "Categoria" }, automatico["categoria"].Select(p => p.Name).ToArray());

        IPropertyPathProvider providerComConfig = CriarProvider(new ProdutoConfigColapsaCategoria());
        var configurado = providerComConfig.GetPaths<Produto>();

        Assert.Equal(new[] { "Categoria", "Nome" }, configurado["categoria"].Select(p => p.Name).ToArray());
    }

    // ----- Ignore -----

    [Fact]
    public void Ignore_RemoveAliasDoMapa()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigIgnoraPeso());

        var caminhos = provider.GetPaths<Produto>();

        Assert.False(caminhos.ContainsKey("peso"));
    }

    // ----- OnlyMapped -----

    [Fact]
    public void OnlyMapped_DeixaApenasOsConfigurados()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigOnlyMapped());

        var caminhos = provider.GetPaths<Produto>();

        Assert.Equal(2, caminhos.Count);
        Assert.True(caminhos.ContainsKey("nome"));
        Assert.True(caminhos.ContainsKey("categoria"));
    }

    [Fact]
    public void OnlyMapped_PropriedadeSensivelNaoMapeada_DeixaDeSerConsultavel()
    {
        var registry = new QueryableConfigurationRegistry();
        registry.Register(new ProdutoConfigOnlyMapped());
        var provider = new ReflectionPropertyPathProvider(registry);
        var filterBuilder = new FilterBuilder(provider);

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            filterBuilder.BuildPredicate<Produto>(new Dictionary<string, string> { ["numeroopcional"] = "5" }));

        Assert.Contains("numeroopcional", ex.Message);
    }

    // ----- modo permissivo (decisão 1): sem OnlyMapped, tudo continua consultável -----

    [Fact]
    public void SemOnlyMapped_TudoContinuaConsultavel()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigColapsaCategoria());

        var caminhos = provider.GetPaths<Produto>();

        Assert.True(caminhos.ContainsKey("peso"));
        Assert.True(caminhos.ContainsKey("ativo"));
        Assert.True(caminhos.ContainsKey("valor")); // alias de [Queryable] em Preco, intacto
    }

    // ----- extração de caminho: rejeição de expressões inválidas -----

    [Fact]
    public void For_ComChamadaDeMetodo_LancaArgumentException()
    {
        var harness = new HarnessProdutoQueryConfiguration();

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            harness.ChamarFor(p => p.Nome.ToUpper()));

        Assert.Contains("ToUpper", ex.Message);
    }

    [Fact]
    public void For_ComIndexador_LancaArgumentException()
    {
        var harness = new HarnessProdutoQueryConfiguration();

        Assert.Throws<ArgumentException>(() =>
            harness.ChamarFor(p => p.Categoria.Produtos[0]));
    }

    [Fact]
    public void For_ComCampo_LancaArgumentException()
    {
        var harness = new HarnessComCampoQueryConfiguration();

        Assert.Throws<ArgumentException>(() =>
            harness.ChamarFor(c => c.CampoPublico));
    }

    [Fact]
    public void Ignore_ComExpressaoInvalida_LancaArgumentException()
    {
        var harness = new HarnessProdutoQueryConfiguration();

        Assert.Throws<ArgumentException>(() =>
            harness.ChamarIgnore(p => p.Nome.ToUpper()));
    }

    // ----- As(alias) inválido -----

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void As_VazioOuNulo_LancaArgumentException(string? alias)
    {
        var harness = new HarnessProdutoQueryConfiguration();
        QueryablePropertyBuilder<Produto> builder = harness.ChamarFor(p => p.Nome);

        Assert.Throws<ArgumentException>(() => builder.As(alias!));
    }

    // ----- case-insensitivity do alias configurado -----

    [Fact]
    public void AliasConfigurado_EhCaseInsensitive()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigAliasCaseVariado());

        var caminhos = provider.GetPaths<Produto>();

        Assert.True(caminhos.TryGetValue("categoria", out var viaMinuscula));
        Assert.True(caminhos.TryGetValue("CATEGORIA", out var viaMaiuscula));
        Assert.Equal(viaMinuscula, viaMaiuscula);
    }

    // ----- cache continua valendo -----

    [Fact]
    public void GetPaths_MesmoTipoConfigurado_DevolveMesmaInstanciaEmCache()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigColapsaCategoria());

        var primeira = provider.GetPaths<Produto>();
        var segunda = provider.GetPaths<Produto>();

        Assert.Same(primeira, segunda);
    }

    // ----- ponta a ponta: FilterBuilder com provider configurado -----

    [Fact]
    public void FiltroPontaAPonta_AliasColapsado_FiltraCorretamente()
    {
        IPropertyPathProvider provider = CriarProvider(new ProdutoConfigColapsaCategoria());
        var filterBuilder = new FilterBuilder(provider);

        var produtos = new List<Produto>
        {
            new() { Nome = "Caneta", Categoria = new Categoria { Nome = "Papelaria" } },
            new() { Nome = "Grampeador", Categoria = new Categoria { Nome = "Escritorio" } }
        };

        var predicate = filterBuilder.BuildPredicate<Produto>(new Dictionary<string, string> { ["categoria"] = "Papelaria" });
        List<Produto> resultado = produtos.AsQueryable().Where(predicate).ToList();

        Assert.Single(resultado);
        Assert.Equal("Caneta", resultado[0].Nome);
    }
}
