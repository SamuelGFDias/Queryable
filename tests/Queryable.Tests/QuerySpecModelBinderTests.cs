using Microsoft.Extensions.Primitives;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Filtering;
using Xunit;

namespace Queryable.Tests;

/// <summary>
/// Suíte da correção de qualidade da Etapa 5: cobre <see cref="QuerySpecModelBinder{T}.BuildSpec"/>,
/// o método extraído de <see cref="QuerySpecModelBinder{T}.BindModelAsync"/> para permitir testar o
/// binder sem infraestrutura de ASP.NET Core (sem <c>HttpContext</c>/<c>ModelBindingContext</c>
/// fake) — bastam pares chave/valor de query string, exatamente o que <c>BuildSpec</c> recebe.
/// </summary>
public class QuerySpecModelBinderTests
{
    private static QuerySpec<Produto> BuildSpec(params (string Key, string Value)[] pairs) =>
        QuerySpecModelBinder<Produto>.BuildSpec(
            pairs.Select(p => new KeyValuePair<string, StringValues>(p.Key, p.Value)));

    // ---------------------------------------------------------------------------------------
    // page / pageSize / sort / skipTotalCount
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BuildSpec_Page_ReconhecidoCaseInsensitive()
    {
        QuerySpec<Produto> spec = BuildSpec(("PaGe", "3"));

        Assert.Equal(3, spec.Page);
    }

    [Fact]
    public void BuildSpec_PageSize_ReconhecidoCaseInsensitive()
    {
        QuerySpec<Produto> spec = BuildSpec(("PAGESIZE", "25"));

        Assert.Equal(25, spec.PageSize);
    }

    [Fact]
    public void BuildSpec_Sort_ReconhecidoCaseInsensitive()
    {
        QuerySpec<Produto> spec = BuildSpec(("SoRt", "-nome,preco"));

        Assert.Equal("-nome,preco", spec.Sort);
    }

    [Fact]
    public void BuildSpec_SkipTotalCount_ReconhecidoCaseInsensitive()
    {
        QuerySpec<Produto> spec = BuildSpec(("SKIPTOTALCOUNT", "true"));

        Assert.True(spec.SkipTotalCount);
    }

    [Fact]
    public void BuildSpec_Page_ValorInvalido_MantemDefault()
    {
        QuerySpec<Produto> spec = BuildSpec(("page", "abc"));

        Assert.Equal(1, spec.Page);
    }

    [Fact]
    public void BuildSpec_Page_MenorOuIgualAZero_MantemDefault()
    {
        QuerySpec<Produto> spec = BuildSpec(("page", "0"));

        Assert.Equal(1, spec.Page);
    }

    [Fact]
    public void BuildSpec_PageSize_ValorInvalido_MantemDefault()
    {
        QuerySpec<Produto> spec = BuildSpec(("pageSize", "xyz"));

        Assert.Equal(10, spec.PageSize);
    }

    [Fact]
    public void BuildSpec_PageSize_MenorOuIgualAZero_MantemDefault()
    {
        QuerySpec<Produto> spec = BuildSpec(("pageSize", "-5"));

        Assert.Equal(10, spec.PageSize);
    }

    // ---------------------------------------------------------------------------------------
    // Chave desconhecida / padrão Filters[chave] do Swagger
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BuildSpec_ChaveDesconhecida_VaiParaFilters_ComChaveEmMinusculas()
    {
        QuerySpec<Produto> spec = BuildSpec(("Nome__Contains", "cane"));

        Assert.Equal("cane", spec.Filters["nome__contains"]);
        Assert.Single(spec.Filters);
    }

    [Fact]
    public void BuildSpec_PadraoSwaggerFiltersColchete_ViraEntradaLimpa()
    {
        QuerySpec<Produto> spec = BuildSpec(("Filters[nome]", "caneta"));

        Assert.Equal("caneta", spec.Filters["nome"]);
        Assert.False(spec.Filters.ContainsKey("filters[nome]"));
    }

    // ---------------------------------------------------------------------------------------
    // filter (mini-linguagem) — o caso central desta correção
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BuildSpec_Filter_EParseadoParaSpecFilter_ENaoApareceEmFilters()
    {
        QuerySpec<Produto> spec = BuildSpec(("filter", "nome__contains=cane and ativo=true"));

        Assert.NotNull(spec.Filter);
        var group = Assert.IsType<FilterGroup>(spec.Filter);
        Assert.Equal(FilterLogic.And, group.Logic);
        Assert.Equal(2, group.Children.Count);

        Assert.False(spec.Filters.ContainsKey("filter"));
        Assert.Empty(spec.Filters);
    }

    [Fact]
    public void BuildSpec_Filter_ReconhecidoCaseInsensitive()
    {
        QuerySpec<Produto> spec = BuildSpec(("FiLtEr", "ativo=true"));

        Assert.NotNull(spec.Filter);
        Assert.False(spec.Filters.ContainsKey("filter"));
    }

    [Fact]
    public void BuildSpec_Filter_ComExpressaoInvalida_PropagaErroDeSintaxe()
    {
        Assert.Throws<FilterExpressionSyntaxException>(() => BuildSpec(("filter", "nome__contains=")));
    }

    [Fact]
    public void BuildSpec_Filter_Ausente_DeixaSpecFilterNuloENaoAlteraOutrosCampos()
    {
        QuerySpec<Produto> spec = BuildSpec(("nome", "caneta"));

        Assert.Null(spec.Filter);
        Assert.Equal("caneta", spec.Filters["nome"]);
    }

    [Fact]
    public void BuildSpec_Filter_VazioOuSoEspacos_DeixaSpecFilterNulo()
    {
        QuerySpec<Produto> spec = BuildSpec(("filter", "   "));

        Assert.Null(spec.Filter);
        Assert.False(spec.Filters.ContainsKey("filter"));
    }
}
