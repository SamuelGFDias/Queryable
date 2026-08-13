using Xunit;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Filtering;

namespace Queryable.Tests;

public class RequestQueryExtensionsTests
{
    [Fact]
    public void ToQuerySpec_RequestNulo_LancaArgumentNullException()
    {
        RequestQuery? request = null;

        Assert.Throws<ArgumentNullException>(() => request!.ToQuerySpec<Produto>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToQuerySpec_QueryFilterNuloVazioOuEspacos_ResultaEmFiltersVazio(string? queryFilter)
    {
        var request = new RequestQuery { QueryFilter = queryFilter };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Empty(spec.Filters);
    }

    [Fact]
    public void ToQuerySpec_SeparadorVirgula_GeraDoisFiltros()
    {
        var request = new RequestQuery { QueryFilter = "nome=ana,ativo=true" };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Equal(2, spec.Filters.Count);
        Assert.Equal("ana", spec.Filters["nome"]);
        Assert.Equal("true", spec.Filters["ativo"]);
    }

    [Fact]
    public void ToQuerySpec_SeparadorPontoEVirgula_PreservaValorCsvDoOperadorIn()
    {
        var request = new RequestQuery { QueryFilter = "id__in=1,2,3;ativo=true" };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Equal(2, spec.Filters.Count);
        Assert.Equal("1,2,3", spec.Filters["id__in"]);
        Assert.Equal("true", spec.Filters["ativo"]);
    }

    [Fact]
    public void ToQuerySpec_DivideNoPrimeiroIgual_ValorPodeConterIgual()
    {
        var request = new RequestQuery { QueryFilter = "nome=a=b" };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Single(spec.Filters);
        Assert.Equal("a=b", spec.Filters["nome"]);
    }

    [Fact]
    public void ToQuerySpec_ItensVaziosEntreSeparadores_SaoIgnorados()
    {
        var request = new RequestQuery { QueryFilter = "nome=ana,,ativo=true" };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Equal(2, spec.Filters.Count);
    }

    [Fact]
    public void ToQuerySpec_FazTrimNaChaveENoValor_EChaveEhNormalizadaParaMinuscula()
    {
        var request = new RequestQuery { QueryFilter = " Nome = Ana " };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.True(spec.Filters.ContainsKey("nome"));
        Assert.Equal("Ana", spec.Filters["nome"]);
    }

    [Fact]
    public void ToQuerySpec_ChaveDuplicada_UltimoValorPrevalece()
    {
        var request = new RequestQuery { QueryFilter = "nome=Ana,nome=Bia" };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Single(spec.Filters);
        Assert.Equal("Bia", spec.Filters["nome"]);
    }

    [Fact]
    public void ToQuerySpec_ItemSemIgual_LancaArgumentException()
    {
        var request = new RequestQuery { QueryFilter = "nomeSemIgual" };

        Assert.Throws<ArgumentException>(() => request.ToQuerySpec<Produto>());
    }

    [Fact]
    public void ToQuerySpec_ChaveVazia_LancaArgumentException()
    {
        var request = new RequestQuery { QueryFilter = "=true" };

        Assert.Throws<ArgumentException>(() => request.ToQuerySpec<Produto>());
    }

    [Fact]
    public void ToQuerySpec_CopiaSortPagePageSizeESkipTotalCount()
    {
        var request = new RequestQuery
        {
            Sort = "-nome",
            Page = 3,
            PageSize = 20,
            SkipTotalCount = true
        };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Equal("-nome", spec.Sort);
        Assert.Equal(3, spec.Page);
        Assert.Equal(20, spec.PageSize);
        Assert.True(spec.SkipTotalCount);
    }

    [Fact]
    public void ToQuerySpec_PageEPageSizeMenorOuIgualAZero_MantemDefault()
    {
        var request = new RequestQuery
        {
            Page = 0,
            PageSize = -5
        };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Equal(1, spec.Page);
        Assert.Equal(10, spec.PageSize);
    }

    [Fact]
    public void ToQuerySpec_FilterNulo_ResultaEmFilterNuloNoQuerySpec()
    {
        var request = new RequestQuery();

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Null(spec.Filter);
    }

    [Fact]
    public void ToQuerySpec_FilterPreenchido_ChegaAoQuerySpecSemAlteracao()
    {
        var filter = new FilterGroup(FilterLogic.Or,
        [
            new FilterCondition("nome", "eq", "ana"),
            new FilterCondition("nome", "eq", "bia")
        ]);
        var request = new RequestQuery { Filter = filter };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.Same(filter, spec.Filter);
    }
}
