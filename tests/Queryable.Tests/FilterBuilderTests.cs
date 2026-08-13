using Xunit;
using Queryable.Builders;

namespace Queryable.Tests;

public class FilterBuilderTests
{
    private static readonly Guid Produto1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Produto2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Produto3Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid IdOpcionalProduto1 = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static List<Produto> CriarProdutos() =>
    [
        new Produto
        {
            Id = Produto1Id,
            Nome = "Caneta Azul",
            Preco = 10m,
            Ativo = true,
            CriadoEm = new DateTime(2024, 1, 10),
            DataOnly = new DateOnly(2024, 1, 10),
            HoraOnly = new TimeOnly(8, 30),
            Status = Status.Ativo,
            NumeroOpcional = 5,
            IdOpcional = IdOpcionalProduto1,
            Categoria = new Categoria { Id = 1, Nome = "Papelaria" }
        },
        new Produto
        {
            Id = Produto2Id,
            Nome = "Caderno",
            Preco = 25m,
            Ativo = false,
            CriadoEm = new DateTime(2024, 3, 15),
            DataOnly = new DateOnly(2024, 3, 15),
            HoraOnly = new TimeOnly(14, 0),
            Status = Status.Inativo,
            NumeroOpcional = null,
            IdOpcional = null,
            Categoria = new Categoria { Id = 2, Nome = "Escritorio" }
        },
        new Produto
        {
            Id = Produto3Id,
            Nome = "Lapis",
            Preco = 5m,
            Ativo = true,
            CriadoEm = new DateTime(2024, 6, 1),
            DataOnly = new DateOnly(2024, 6, 1),
            HoraOnly = new TimeOnly(9, 0),
            Status = Status.Pendente,
            NumeroOpcional = 7,
            IdOpcional = null,
            Categoria = new Categoria { Id = 1, Nome = "Papelaria" }
        }
    ];

    private static List<Produto> Filtrar(IDictionary<string, string> filtros)
    {
        var builder = new FilterBuilder();
        var predicate = builder.BuildPredicate<Produto>(filtros);
        return CriarProdutos().AsQueryable().Where(predicate).ToList();
    }

    // ----- Matriz eq/neq por tipo (também cobre "sem sufixo => eq") -----

    [Theory]
    [InlineData("nome", "Caderno", 1)] // string, sem sufixo => eq
    [InlineData("nome__eq", "Caderno", 1)] // string, eq explícito
    [InlineData("nome__neq", "Caderno", 2)] // string, neq
    [InlineData("ativo", "true", 2)] // bool, eq
    [InlineData("ativo__neq", "true", 1)] // bool, neq
    [InlineData("valor", "10", 1)] // decimal, eq (alias)
    [InlineData("valor__neq", "10", 2)] // decimal, neq
    [InlineData("id", "11111111-1111-1111-1111-111111111111", 1)] // Guid, eq
    [InlineData("id__neq", "11111111-1111-1111-1111-111111111111", 2)] // Guid, neq
    [InlineData("status", "Pendente", 1)] // enum, eq
    [InlineData("status__neq", "Pendente", 2)] // enum, neq
    [InlineData("dataonly", "2024-01-10", 1)] // DateOnly, eq
    [InlineData("dataonly__neq", "2024-01-10", 2)] // DateOnly, neq
    [InlineData("horaonly", "09:00", 1)] // TimeOnly, eq
    [InlineData("horaonly__neq", "09:00", 2)] // TimeOnly, neq
    [InlineData("numeroopcional", "5", 1)] // int (via int?), eq
    public void EqNeq_FiltraCorretamentePorTipo(string chave, string valor, int quantidadeEsperada)
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { [chave] = valor });

        Assert.Equal(quantidadeEsperada, resultado.Count);
    }

    // ----- gt/lt/gte/lte em numérico e data -----

    [Theory]
    [InlineData("valor__gt", "10", 1)] // apenas 25
    [InlineData("valor__gte", "10", 2)] // 10 e 25
    [InlineData("valor__lt", "10", 1)] // apenas 5
    [InlineData("valor__lte", "10", 2)] // 5 e 10
    [InlineData("criadoem__gt", "2024-03-15", 1)] // apenas 2024-06-01
    [InlineData("criadoem__gte", "2024-03-15", 2)] // 2024-03-15 e 2024-06-01
    [InlineData("criadoem__lt", "2024-03-15", 1)] // apenas 2024-01-10
    [InlineData("criadoem__lte", "2024-03-15", 2)] // 2024-01-10 e 2024-03-15
    [InlineData("dataonly__gt", "2024-01-10", 2)]
    [InlineData("dataonly__lt", "2024-03-15", 1)]
    public void GtLtGteLte_FiltraCorretamente(string chave, string valor, int quantidadeEsperada)
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { [chave] = valor });

        Assert.Equal(quantidadeEsperada, resultado.Count);
    }

    [Fact]
    public void Eq_Decimal_ComPontoDecimal_UsaCulturaInvariante()
    {
        // A conversão de valores escalares em FilterBuilder.ConvertScalar é feita com
        // CultureInfo.InvariantCulture por design, independentemente da cultura configurada
        // na máquina que executa o processo. Assim, "." é sempre separador decimal, nunca
        // separador de milhar, mesmo em locales como pt-BR.
        var produtos = new List<Produto>
        {
            new() { Nome = "MilECinquenta", Preco = 1050m },
            new() { Nome = "DezEMeio", Preco = 10.5m }
        };

        var builder = new FilterBuilder();
        var predicate = builder.BuildPredicate<Produto>(new Dictionary<string, string> { ["valor"] = "10.50" });
        List<Produto> resultado = produtos.AsQueryable().Where(predicate).ToList();

        Assert.Single(resultado);
        Assert.Equal("DezEMeio", resultado[0].Nome);
    }

    [Theory]
    [InlineData("valor__gte", "10.50", 2)] // 10.5 e 25.99 (>= 10.5)
    [InlineData("valor__lte", "10.50", 2)] // 5.25 e 10.5 (<= 10.5)
    public void GteLte_Decimal_ComPontoDecimal_UsaCulturaInvariante(string chave, string valor, int quantidadeEsperada)
    {
        var produtos = new List<Produto>
        {
            new() { Nome = "A", Preco = 5.25m },
            new() { Nome = "B", Preco = 10.5m },
            new() { Nome = "C", Preco = 25.99m }
        };

        var builder = new FilterBuilder();
        var predicate = builder.BuildPredicate<Produto>(new Dictionary<string, string> { [chave] = valor });
        List<Produto> resultado = produtos.AsQueryable().Where(predicate).ToList();

        Assert.Equal(quantidadeEsperada, resultado.Count);
    }

    [Fact]
    public void Eq_Double_ComPontoDecimal_UsaCulturaInvariante()
    {
        var produtos = new List<Produto>
        {
            new() { Nome = "Leve", Peso = 1.5 },
            new() { Nome = "Pesado", Peso = 1050.0 }
        };

        var builder = new FilterBuilder();
        var predicate = builder.BuildPredicate<Produto>(new Dictionary<string, string> { ["peso"] = "1.50" });
        List<Produto> resultado = produtos.AsQueryable().Where(predicate).ToList();

        Assert.Single(resultado);
        Assert.Equal("Leve", resultado[0].Nome);
    }

    [Fact]
    public void In_Decimal_ComValoresContendoPontoDecimal_UsaCulturaInvariante()
    {
        var produtos = new List<Produto>
        {
            new() { Nome = "A", Preco = 10.5m },
            new() { Nome = "B", Preco = 20.25m },
            new() { Nome = "C", Preco = 1050m }
        };

        var builder = new FilterBuilder();
        var predicate = builder.BuildPredicate<Produto>(new Dictionary<string, string> { ["valor__in"] = "10.5,20.25" });
        List<Produto> resultado = produtos.AsQueryable().Where(predicate).ToList();

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Nome == "A");
        Assert.Contains(resultado, p => p.Nome == "B");
    }

    [Fact]
    public void Eq_DateOnly_FormatoIso_ContinuaCorreto()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["dataonly"] = "2024-02-01" });

        Assert.Empty(resultado);

        resultado = Filtrar(new Dictionary<string, string> { ["dataonly"] = "2024-01-10" });

        Assert.Single(resultado);
    }

    // ----- contains -----

    [Fact]
    public void Contains_FiltraSubstringEmString()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["nome__contains"] = "ade" });

        Assert.Single(resultado);
        Assert.Equal("Caderno", resultado[0].Nome);
    }

    [Fact]
    public void Contains_LancaNotSupported_QuandoPropriedadeNaoEhString()
    {
        Assert.Throws<NotSupportedException>(() =>
            Filtrar(new Dictionary<string, string> { ["valor__contains"] = "10" }));
    }

    // ----- in -----

    [Fact]
    public void In_Int_FiltraPorListaDeValores()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["numeroopcional__in"] = "5,7" });

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Nome == "Caneta Azul");
        Assert.Contains(resultado, p => p.Nome == "Lapis");
    }

    [Fact]
    public void In_String_FiltraPorListaDeValores()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["nome__in"] = "Caderno,Lapis" });

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void In_Guid_FiltraPorListaDeValores()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string>
        {
            ["id__in"] = $"{Produto1Id},{Produto2Id}"
        });

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void In_Enum_FiltraPorNome()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["status__in"] = "Ativo,Pendente" });

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, p => Assert.True(p.Status is Status.Ativo or Status.Pendente));
    }

    [Fact]
    public void In_GuidNullable_ComLiteralNull_FiltraNulosEValor()
    {
        // IdOpcional: produto1 = IdOpcionalProduto1, produto2 = null, produto3 = null.
        List<Produto> resultado = Filtrar(new Dictionary<string, string>
        {
            ["idopcional__in"] = $"null,{IdOpcionalProduto1}"
        });

        Assert.Equal(3, resultado.Count);
    }

    [Fact]
    public void In_ComEspacosAoRedorDosItens_FazTrimAntesDeConverter()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["nome__in"] = " Caderno , Lapis " });

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void In_ComItensVaziosEntreSeparadores_IgnoraOsVazios()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["nome__in"] = "Caderno,,Lapis" });

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void In_ComListaVazia_LancaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["nome__in"] = ",, ," }));
    }

    // ----- literal "null" em eq sobre Nullable<T> -----

    [Fact]
    public void Eq_LiteralNull_SobreNullable_FiltraItensComValorNulo()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["idopcional"] = "null" });

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, p => Assert.Null(p.IdOpcional));
    }

    // ----- campo desconhecido -----

    [Fact]
    public void CampoDesconhecido_LancaArgumentException_ComNomeDoCampoNaMensagem()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["campoinexistente"] = "x" }));

        Assert.Contains("campoinexistente", ex.Message);
    }

    // ----- múltiplos filtros combinam com AND -----

    [Fact]
    public void MultiplosFiltros_CombinamComAnd()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string>
        {
            ["ativo"] = "true",
            ["categoria.nome"] = "Papelaria"
        });

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, p => Assert.True(p.Ativo));
        Assert.All(resultado, p => Assert.Equal("Papelaria", p.Categoria.Nome));
    }

    // ----- dicionário vazio -----

    [Fact]
    public void DicionarioVazio_PredicadoAceitaTodosOsItens()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string>());

        Assert.Equal(3, resultado.Count);
    }

    // ----- case-insensitivity da chave -----

    [Theory]
    [InlineData("NOME")]
    [InlineData("Nome")]
    [InlineData("nOmE")]
    public void ChaveDeFiltro_EhCaseInsensitive(string chave)
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { [chave] = "Caderno" });

        Assert.Single(resultado);
    }

    [Fact]
    public void OperadorNoSufixoDaChave_EhCaseInsensitive()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["VALOR__GT"] = "5" });

        Assert.Equal(2, resultado.Count);
    }

    // ----- alias vs. nome original -----

    [Fact]
    public void FiltroPorAlias_Valor_Funciona()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["valor"] = "10" });

        Assert.Single(resultado);
        Assert.Equal("Caneta Azul", resultado[0].Nome);
    }

    [Fact]
    public void FiltroPeloNomeOriginal_Preco_LancaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["preco"] = "10" }));
    }

    // ----- caminho aninhado -----

    [Fact]
    public void FiltroPorCaminhoAninhado_CategoriaNome_Funciona()
    {
        List<Produto> resultado = Filtrar(new Dictionary<string, string> { ["categoria.nome"] = "Papelaria" });

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, p => Assert.Equal("Papelaria", p.Categoria.Nome));
    }
}
