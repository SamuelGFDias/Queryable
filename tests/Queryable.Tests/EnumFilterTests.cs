using Xunit;
using Queryable.Builders;

namespace Queryable.Tests;

public class EnumFilterTests
{
    private enum Situacao
    {
        Liberada = 1,
        Bloqueada = 2
    }

    [Flags]
    private enum Permissao
    {
        None = 0,
        Read = 1,
        Write = 2,
        Execute = 4
    }

    private enum ComSinal : sbyte
    {
        A = -1,
        B = 1
    }

    private sealed class Registro
    {
        public int Id { get; set; }
        public Situacao Situacao { get; set; }
        public Situacao? SituacaoOpcional { get; set; }
        public Permissao Permissao { get; set; }
        public ComSinal ComSinal { get; set; }
    }

    private static List<Registro> CriarRegistros() =>
    [
        new Registro { Id = 1, Situacao = Situacao.Liberada, SituacaoOpcional = Situacao.Liberada, Permissao = Permissao.Read, ComSinal = ComSinal.A },
        new Registro { Id = 2, Situacao = Situacao.Bloqueada, SituacaoOpcional = null, Permissao = Permissao.Write, ComSinal = ComSinal.B }
    ];

    private static List<Registro> Filtrar(Dictionary<string, string> filtros)
    {
        var predicado = new FilterBuilder().BuildPredicate<Registro>(filtros);
        return CriarRegistros().AsQueryable().Where(predicado).ToList();
    }

    [Fact]
    public void NomeValidoCaseInsensitiveFiltra()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["situacao"] = "liberada" });

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].Id);
    }

    [Fact]
    public void NumeroValidoFiltra()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["situacao"] = "2" });

        Assert.Single(resultado);
        Assert.Equal(2, resultado[0].Id);
    }

    [Fact]
    public void NomeInvalidoLancaArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["situacao"] = "Bananas" }));

        Assert.Contains("não é válido para o enum", ex.Message);
    }

    [Fact]
    public void NumeroForaDoEnumLancaArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["situacao"] = "999" }));

        Assert.Contains("não é válido para o enum", ex.Message);
    }

    [Fact]
    public void InComValorForaDoEnumLancaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["situacao__in"] = "1,999" }));
    }

    [Fact]
    public void InComValoresValidosFiltraOsDois()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["situacao__in"] = "1,2" });

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void FlagsValorCompostoNumericoAceito()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["permissao"] = "3" });

        // Nenhum registro tem exatamente Read|Write, mas o valor deve ser aceito sem exceção.
        Assert.Empty(resultado);
    }

    [Fact]
    public void FlagsNomeCompostoAceito()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["permissao"] = "Read, Write" });

        Assert.Empty(resultado);
    }

    [Fact]
    public void FlagsBitForaDoEnumLancaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["permissao"] = "8" }));
    }

    [Fact]
    public void FlagsZeroAceito()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["permissao"] = "0" });

        Assert.Empty(resultado);
    }

    [Fact]
    public void EnumComSinalValorValidoAceito()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["comsinal"] = "-1" });

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].Id);
    }

    [Fact]
    public void EnumComSinalValorInvalidoLancaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["comsinal"] = "5" }));
    }

    [Fact]
    public void SituacaoOpcionalNullContinuaFiltrandoNulos()
    {
        List<Registro> resultado = Filtrar(new Dictionary<string, string> { ["situacaoopcional"] = "null" });

        Assert.Single(resultado);
        Assert.Equal(2, resultado[0].Id);
    }

    [Fact]
    public void SituacaoOpcionalInvalidaLancaArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Filtrar(new Dictionary<string, string> { ["situacaoopcional"] = "999" }));
    }
}
