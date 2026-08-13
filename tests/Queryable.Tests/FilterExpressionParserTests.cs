using Xunit;
using Queryable.Builders;
using Queryable.Filtering;

namespace Queryable.Tests;

/// <summary>
/// Suíte da Etapa 5: <see cref="FilterExpressionParser"/>, a mini-linguagem textual de filtros
/// compostos para uso em query string. Cobre a tabela de exemplos da seção 4 de
/// <c>docs/proposta-filtros-compostos.md</c> (válidos e inválidos), precedência, aspas/escape,
/// o operador <c>in</c>, e um caso de ponta a ponta com <c>List&lt;T&gt;.AsQueryable()</c>
/// provando que uma expressão com <c>or</c> real é compilada e filtra corretamente.
/// </summary>
public class FilterExpressionParserTests
{
    // ---------------------------------------------------------------------------------------
    // Válidos
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Parse_CondicaoSimples_SemOperador_AssumeEq()
    {
        FilterNode node = FilterExpressionParser.Parse("nome=joao");

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("nome", condition.Field);
        Assert.Equal("eq", condition.Operator);
        Assert.Equal("joao", condition.Value);
    }

    [Fact]
    public void Parse_DuasCondicoesComAnd_GeraFilterGroupAnd()
    {
        FilterNode node = FilterExpressionParser.Parse("nome__contains=jo and ativo=true");

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.And, group.Logic);
        Assert.Equal(2, group.Children.Count);

        var first = Assert.IsType<FilterCondition>(group.Children[0]);
        Assert.Equal("nome", first.Field);
        Assert.Equal("contains", first.Operator);
        Assert.Equal("jo", first.Value);

        var second = Assert.IsType<FilterCondition>(group.Children[1]);
        Assert.Equal("ativo", second.Field);
        Assert.Equal("eq", second.Operator);
        Assert.Equal("true", second.Value);
    }

    [Fact]
    public void Parse_ParentesesComOr_EAndExterno_RespeitaAgrupamento()
    {
        FilterNode node = FilterExpressionParser.Parse(
            "(nome__contains=ana or nome__contains=joao) and ativo=true");

        var outerGroup = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.And, outerGroup.Logic);
        Assert.Equal(2, outerGroup.Children.Count);

        var innerOr = Assert.IsType<FilterGroup>(outerGroup.Children[0]);
        Assert.Equal(FilterLogic.Or, innerOr.Logic);
        Assert.Equal(2, innerOr.Children.Count);

        var innerFirst = Assert.IsType<FilterCondition>(innerOr.Children[0]);
        Assert.Equal("ana", innerFirst.Value);
        var innerSecond = Assert.IsType<FilterCondition>(innerOr.Children[1]);
        Assert.Equal("joao", innerSecond.Value);

        var ativo = Assert.IsType<FilterCondition>(outerGroup.Children[1]);
        Assert.Equal("ativo", ativo.Field);
    }

    [Fact]
    public void Parse_Not_GeraFilterNot()
    {
        FilterNode node = FilterExpressionParser.Parse("not ativo=false");

        var not = Assert.IsType<FilterNot>(node);
        var inner = Assert.IsType<FilterCondition>(not.Inner);
        Assert.Equal("ativo", inner.Field);
        Assert.Equal("false", inner.Value);
    }

    [Fact]
    public void Parse_OperadorIn_ListaEntreParenteses_GeraCsv()
    {
        FilterNode node = FilterExpressionParser.Parse("id__in=(1,2,3)");

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("id", condition.Field);
        Assert.Equal("in", condition.Operator);
        Assert.Equal("1,2,3", condition.Value);
    }

    [Fact]
    public void Parse_ValorEntreAspasColideComPalavraChave_FuncionaComoValorLiteral()
    {
        FilterNode node = FilterExpressionParser.Parse("nome=\"and joão\"");

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("nome", condition.Field);
        Assert.Equal("and joão", condition.Value);
    }

    [Fact]
    public void Parse_PrecedenciaSemParenteses_OrLigaMaisFracoQueAnd()
    {
        // a or b and c === a or (b and c)
        FilterNode node = FilterExpressionParser.Parse("a=1 or b=2 and c=3");

        var orGroup = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.Or, orGroup.Logic);
        Assert.Equal(2, orGroup.Children.Count);

        var a = Assert.IsType<FilterCondition>(orGroup.Children[0]);
        Assert.Equal("a", a.Field);

        var andGroup = Assert.IsType<FilterGroup>(orGroup.Children[1]);
        Assert.Equal(FilterLogic.And, andGroup.Logic);
        Assert.Equal(2, andGroup.Children.Count);
        Assert.Equal("b", Assert.IsType<FilterCondition>(andGroup.Children[0]).Field);
        Assert.Equal("c", Assert.IsType<FilterCondition>(andGroup.Children[1]).Field);
    }

    [Fact]
    public void Parse_Not_LigaMaisForteQueAnd()
    {
        // not a and b === (not a) and b
        FilterNode node = FilterExpressionParser.Parse("not a=1 and b=2");

        var andGroup = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.And, andGroup.Logic);
        Assert.Equal(2, andGroup.Children.Count);

        var not = Assert.IsType<FilterNot>(andGroup.Children[0]);
        Assert.Equal("a", Assert.IsType<FilterCondition>(not.Inner).Field);

        Assert.Equal("b", Assert.IsType<FilterCondition>(andGroup.Children[1]).Field);
    }

    [Theory]
    [InlineData("a=1 AND b=2")]
    [InlineData("a=1 And b=2")]
    [InlineData("a=1 aNd b=2")]
    public void Parse_PalavraChaveAnd_ECaseInsensitive(string expression)
    {
        FilterNode node = FilterExpressionParser.Parse(expression);

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.And, group.Logic);
    }

    [Theory]
    [InlineData("a=1 OR b=2")]
    [InlineData("a=1 Or b=2")]
    public void Parse_PalavraChaveOr_ECaseInsensitive(string expression)
    {
        FilterNode node = FilterExpressionParser.Parse(expression);

        var group = Assert.IsType<FilterGroup>(node);
        Assert.Equal(FilterLogic.Or, group.Logic);
    }

    [Theory]
    [InlineData("NOT a=1")]
    [InlineData("Not a=1")]
    public void Parse_PalavraChaveNot_ECaseInsensitive(string expression)
    {
        FilterNode node = FilterExpressionParser.Parse(expression);

        Assert.IsType<FilterNot>(node);
    }

    [Fact]
    public void Parse_EscapeAspaDupla_DentroDeAspas_ResolveParaAspaLiteral()
    {
        FilterNode node = FilterExpressionParser.Parse("nome=\"a\\\"b\"");

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("a\"b", condition.Value);
    }

    [Fact]
    public void Parse_EscapeBarraInvertida_DentroDeAspas_ResolveParaBarraLiteral()
    {
        FilterNode node = FilterExpressionParser.Parse("nome=\"a\\\\b\"");

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal(@"a\b", condition.Value);
    }

    [Fact]
    public void Parse_ItemDeInEntreAspas_FuncionaComoItemUnico()
    {
        FilterNode node = FilterExpressionParser.Parse("tag__in=(\"a b\",c)");

        var condition = Assert.IsType<FilterCondition>(node);
        Assert.Equal("in", condition.Operator);
        Assert.Equal("a b,c", condition.Value);
    }

    [Fact]
    public void Parse_ExpressaoComOrReal_AplicadaViaFilterBuilder_FiltraCorretamente()
    {
        var produtos = new List<Produto>
        {
            new() { Nome = "Caneta", Ativo = true },
            new() { Nome = "Caderno", Ativo = false },
            new() { Nome = "Lapis", Ativo = true }
        };

        FilterNode tree = FilterExpressionParser.Parse("nome=Caderno or nome=Lapis");
        var builder = new FilterBuilder();
        var predicate = builder.BuildPredicate<Produto>(tree);

        List<Produto> resultado = produtos.AsQueryable().Where(predicate).ToList();

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, p => p.Nome == "Caderno");
        Assert.Contains(resultado, p => p.Nome == "Lapis");
    }

    // ---------------------------------------------------------------------------------------
    // Inválidos
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Parse_ExpressaoNula_LancaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FilterExpressionParser.Parse(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ExpressaoVaziaOuSoEspacos_LancaErroDeSintaxe(string expression)
    {
        Assert.Throws<FilterExpressionSyntaxException>(() => FilterExpressionParser.Parse(expression));
    }

    [Fact]
    public void Parse_PalavraChaveForaDeAspasComoValor_LancaErro()
    {
        var ex = Assert.Throws<FilterExpressionSyntaxException>(
            () => FilterExpressionParser.Parse("nome=and joão"));

        Assert.Contains("and", ex.Message);
    }

    [Fact]
    public void Parse_EspacoForaDeAspasTerminaValorAntesDoEsperado_LancaErro()
    {
        Assert.Throws<FilterExpressionSyntaxException>(
            () => FilterExpressionParser.Parse("nome=joão silva"));
    }

    [Fact]
    public void Parse_ParenteseNaoFechado_LancaErro()
    {
        Assert.Throws<FilterExpressionSyntaxException>(() => FilterExpressionParser.Parse("(nome=ana"));
    }

    [Fact]
    public void Parse_AspasNaoFechadas_LancaErro()
    {
        Assert.Throws<FilterExpressionSyntaxException>(() => FilterExpressionParser.Parse("nome=\"joão"));
    }

    [Fact]
    public void Parse_InForaDeParenteses_LancaErro()
    {
        Assert.Throws<FilterExpressionSyntaxException>(() => FilterExpressionParser.Parse("id__in=1,2,3"));
    }

    [Fact]
    public void Parse_ItemDeInComVirgulaLiteral_LancaErroClaro()
    {
        var ex = Assert.Throws<FilterExpressionSyntaxException>(
            () => FilterExpressionParser.Parse("tag__in=(\"a, b\",c)"));

        Assert.Contains("vírgula", ex.Message);
    }

    [Fact]
    public void Parse_SequenciaDeEscapeInvalida_LancaErro()
    {
        Assert.Throws<FilterExpressionSyntaxException>(
            () => FilterExpressionParser.Parse("nome=\"a\\nb\""));
    }

    [Fact]
    public void Parse_ExceptionExposeAPosicaoAproximada()
    {
        var ex = Assert.Throws<FilterExpressionSyntaxException>(
            () => FilterExpressionParser.Parse("(nome=ana"));

        Assert.True(ex.Position > 0);
    }
}
