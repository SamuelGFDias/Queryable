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

    // ----- navegação profunda (3 níveis): Registro.Pessoa.Documento.Value -----

    private sealed class RegistroConfigAliasProfundo : QueryableConfiguration<Registro>
    {
        public RegistroConfigAliasProfundo()
        {
            For(r => r.Pessoa.Documento.Value).As("documento_pessoa");
        }
    }

    private sealed class RegistroConfigOnlyMappedProfundo : QueryableConfiguration<Registro>
    {
        public RegistroConfigOnlyMappedProfundo()
        {
            OnlyMapped();
            For(r => r.Pessoa.Documento.Value).As("documento_pessoa");
        }
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

    // ----- navegação profunda (3 níveis): Registro.Pessoa.Documento.Value -----

    [Fact]
    public void AliasConfigurado_TresNiveis_ResolveParaCaminhoCompletoNaOrdemCerta()
    {
        IPropertyPathProvider provider = CriarProvider(new RegistroConfigAliasProfundo());

        var caminhos = provider.GetPaths<Registro>();

        Assert.True(caminhos.TryGetValue("documento_pessoa", out List<System.Reflection.PropertyInfo>? caminho));
        Assert.Equal(new[] { "Pessoa", "Documento", "Value" }, caminho!.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void AliasConfigurado_TresNiveis_Coexiste_ComAliasAutomaticoAntigo()
    {
        IPropertyPathProvider provider = CriarProvider(new RegistroConfigAliasProfundo());

        var caminhos = provider.GetPaths<Registro>();

        Assert.True(caminhos.ContainsKey("documento_pessoa"));
        Assert.True(caminhos.ContainsKey("pessoa.documento.value"));
        Assert.Equal(caminhos["documento_pessoa"], caminhos["pessoa.documento.value"]);
    }

    [Fact]
    public void FiltroPontaAPonta_AliasTresNiveis_FiltraCorretamente()
    {
        IPropertyPathProvider provider = CriarProvider(new RegistroConfigAliasProfundo());
        var filterBuilder = new FilterBuilder(provider);

        var registros = new List<Registro>
        {
            new() { Id = 1, Pessoa = new Pessoa { Nome = "Ana", Documento = new Documento { Value = "111" } } },
            new() { Id = 2, Pessoa = new Pessoa { Nome = "Beto", Documento = new Documento { Value = "222" } } }
        };

        var predicate = filterBuilder.BuildPredicate<Registro>(
            new Dictionary<string, string> { ["documento_pessoa"] = "111" });
        List<Registro> resultado = registros.AsQueryable().Where(predicate).ToList();

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].Id);
    }

    [Fact]
    public void OnlyMapped_CaminhoProfundo_ConfiguradoFuncionaEAutomaticoDeixaDeExistir()
    {
        IPropertyPathProvider provider = CriarProvider(new RegistroConfigOnlyMappedProfundo());

        var caminhos = provider.GetPaths<Registro>();

        Assert.True(caminhos.ContainsKey("documento_pessoa"));
        Assert.Equal(new[] { "Pessoa", "Documento", "Value" }, caminhos["documento_pessoa"].Select(p => p.Name).ToArray());
        Assert.False(caminhos.ContainsKey("pessoa.documento.value"));
    }

    [Fact]
    public void AliasComUnderscoreSimples_EhSeguro_DuploUnderscoreContinuaSeparadorDeOperador()
    {
        IPropertyPathProvider provider = CriarProvider(new RegistroConfigAliasProfundo());
        var filterBuilder = new FilterBuilder(provider);

        var registros = new List<Registro>
        {
            new() { Id = 1, Pessoa = new Pessoa { Nome = "Ana", Documento = new Documento { Value = "12345" } } },
            new() { Id = 2, Pessoa = new Pessoa { Nome = "Beto", Documento = new Documento { Value = "99999" } } }
        };

        // Alias simples (sem operador): eq implícito.
        var predicateEq = filterBuilder.BuildPredicate<Registro>(
            new Dictionary<string, string> { ["documento_pessoa"] = "12345" });
        Assert.Single(registros.AsQueryable().Where(predicateEq).ToList());

        // "__" continua sendo o separador de operador, mesmo com "_" simples já presente no alias.
        var predicateContains = filterBuilder.BuildPredicate<Registro>(
            new Dictionary<string, string> { ["documento_pessoa__contains"] = "234" });
        List<Registro> resultado = registros.AsQueryable().Where(predicateContains).ToList();

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].Id);
    }
}
