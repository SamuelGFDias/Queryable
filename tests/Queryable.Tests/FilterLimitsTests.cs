using Queryable.Core;
using Queryable.Extensions;
using Queryable.Filtering;
using Xunit;

namespace Queryable.Tests;

/// <summary>
/// Suíte da Etapa 6: <see cref="FilterLimits"/>, <see cref="FilterLimitValidator"/> e
/// <see cref="FilterLimitExceededException"/> — os tetos de segurança aplicados a uma árvore de
/// filtro composto antes de qualquer compilação para <see cref="System.Linq.Expressions.Expression"/>.
/// Cobre validação isolada (<see cref="FilterLimitValidator.Validate(FilterNode,FilterLimits?)"/>),
/// a checagem de tamanho de string antes de tokenizar em
/// <see cref="FilterExpressionParser.Parse(string,FilterLimits?)"/>, limites customizados, o fato
/// de a validação acontecer antes de qualquer resolução de campo, e a soma de nós na combinação
/// <c>Filter</c> + <c>FilterExpression</c> feita por <see cref="RequestQueryExtensions.ToQuerySpec{T}(RequestQuery,FilterLimits?)"/>.
/// </summary>
public class FilterLimitsTests
{
    // ---------------------------------------------------------------------------------------
    // Helpers de construção de árvore
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Cadeia de <see cref="FilterNot"/> aninhados terminando numa condição folha. O nó mais
    /// profundo (a própria condição) fica exatamente em <paramref name="totalDepth"/>, contando
    /// o nó raiz como profundidade 1 — mesma convenção usada por <see cref="FilterLimitValidator"/>.
    /// </summary>
    private static FilterNode BuildNestedNot(int totalDepth)
    {
        FilterNode node = new FilterCondition("nome", "eq", "x");
        for (int i = 1; i < totalDepth; i++)
            node = new FilterNot(node);
        return node;
    }

    /// <summary>Grupo AND raso com <paramref name="childCount"/> condições folha — total de nós = childCount + 1.</summary>
    private static FilterGroup BuildWideGroup(int childCount, string field = "campo") =>
        new(FilterLogic.And, Enumerable.Range(0, childCount)
            .Select(i => (FilterNode)new FilterCondition($"{field}{i}", "eq", "x"))
            .ToList());

    private static string BuildWideExpression(int conditionCount, string field = "campo") =>
        string.Join(" and ", Enumerable.Range(0, conditionCount).Select(i => $"{field}{i}=x"));

    // ---------------------------------------------------------------------------------------
    // Árvore dentro dos limites
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Validate_ArvoreDentroDeTodosOsLimites_NaoLanca()
    {
        FilterNode tree = new FilterGroup(FilterLogic.And,
        [
            new FilterCondition("nome", "eq", "ana"),
            new FilterNot(new FilterCondition("ativo", "eq", "false")),
            new FilterCondition("id", "in", "1,2,3")
        ]);

        Exception? ex = Record.Exception(() => FilterLimitValidator.Validate(tree));

        Assert.Null(ex);
    }

    // ---------------------------------------------------------------------------------------
    // Profundidade (MaxDepth)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Validate_ProfundidadeIgualAoDefault_NaoLanca()
    {
        FilterNode tree = BuildNestedNot(FilterLimits.Default.MaxDepth);

        Exception? ex = Record.Exception(() => FilterLimitValidator.Validate(tree));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_ProfundidadeAcimaDoMaxDepth_LancaComMensagemCitandoOLimite()
    {
        FilterNode tree = BuildNestedNot(FilterLimits.Default.MaxDepth + 1);

        var ex = Assert.Throws<FilterLimitExceededException>(() => FilterLimitValidator.Validate(tree));

        Assert.Equal(FilterLimitKind.MaxDepth, ex.Limit);
        Assert.Equal(FilterLimits.Default.MaxDepth, ex.Allowed);
        Assert.Contains(FilterLimits.Default.MaxDepth.ToString(), ex.Message);
        Assert.Contains("profundidade", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------
    // Número de nós (MaxNodes)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Validate_NumeroDeNosIgualAoDefault_NaoLanca()
    {
        // childCount + 1 (o próprio grupo) == MaxNodes.
        FilterNode tree = BuildWideGroup(FilterLimits.Default.MaxNodes - 1);

        Exception? ex = Record.Exception(() => FilterLimitValidator.Validate(tree));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_NumeroDeNosAcimaDoMaxNodes_LancaComMensagemCitandoOLimite()
    {
        FilterNode tree = BuildWideGroup(FilterLimits.Default.MaxNodes + 1);

        var ex = Assert.Throws<FilterLimitExceededException>(() => FilterLimitValidator.Validate(tree));

        Assert.Equal(FilterLimitKind.MaxNodes, ex.Limit);
        Assert.Equal(FilterLimits.Default.MaxNodes, ex.Allowed);
        Assert.Contains(FilterLimits.Default.MaxNodes.ToString(), ex.Message);
        Assert.Contains("nós", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------
    // Tamanho da expressão da mini-linguagem (MaxExpressionLength) — ANTES de tokenizar
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Parse_ExpressaoAcimaDoMaxExpressionLength_LancaAntesDeTokenizar_MesmoSendoSintaticamenteInvalida()
    {
        // String só de parênteses abertos, sem nenhum fechamento: se o tamanho não fosse
        // verificado antes de tokenizar/parsear, o erro observado seria de sintaxe (parêntese
        // não fechado), não de limite. Prova que o teto de tamanho é aplicado primeiro.
        string expressaoLongaEInvalida = new string('(', FilterLimits.Default.MaxExpressionLength + 1);

        var ex = Assert.Throws<FilterLimitExceededException>(
            () => FilterExpressionParser.Parse(expressaoLongaEInvalida));

        Assert.Equal(FilterLimitKind.MaxExpressionLength, ex.Limit);
        Assert.Equal(FilterLimits.Default.MaxExpressionLength, ex.Allowed);
        Assert.Contains(FilterLimits.Default.MaxExpressionLength.ToString(), ex.Message);
    }

    [Fact]
    public void Parse_ExpressaoDentroDoMaxExpressionLength_ContinuaParseandoNormalmente()
    {
        FilterNode node = FilterExpressionParser.Parse("nome=ana");

        Assert.IsType<FilterCondition>(node);
    }

    // ---------------------------------------------------------------------------------------
    // Itens de 'in' (MaxInItems)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Validate_ListaInComMaisItensQueMaxInItems_Lanca()
    {
        string csv = string.Join(',', Enumerable.Range(0, FilterLimits.Default.MaxInItems + 1));
        FilterNode tree = new FilterCondition("id", "in", csv);

        var ex = Assert.Throws<FilterLimitExceededException>(() => FilterLimitValidator.Validate(tree));

        Assert.Equal(FilterLimitKind.MaxInItems, ex.Limit);
        Assert.Equal(FilterLimits.Default.MaxInItems, ex.Allowed);
        Assert.Contains(FilterLimits.Default.MaxInItems.ToString(), ex.Message);
    }

    [Fact]
    public void Validate_ListaInComExatamenteMaxInItems_NaoLanca()
    {
        string csv = string.Join(',', Enumerable.Range(0, FilterLimits.Default.MaxInItems));
        FilterNode tree = new FilterCondition("id", "in", csv);

        Exception? ex = Record.Exception(() => FilterLimitValidator.Validate(tree));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_OperadorDiferenteDeIn_NaoContaItensCsv_MesmoComMuitasVirgulas()
    {
        string csv = string.Join(',', Enumerable.Range(0, FilterLimits.Default.MaxInItems + 50));
        FilterNode tree = new FilterCondition("descricao", "eq", csv);

        Exception? ex = Record.Exception(() => FilterLimitValidator.Validate(tree));

        Assert.Null(ex);
    }

    // ---------------------------------------------------------------------------------------
    // Limites customizados
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Validate_LimitesCustomizados_MaxDepthMenor_RejeitaOQueODefaultAceitaria()
    {
        FilterNode tree = BuildNestedNot(3);

        // O default (6) aceita profundidade 3 sem problema.
        Exception? semLimiteCustomizado = Record.Exception(() => FilterLimitValidator.Validate(tree));
        Assert.Null(semLimiteCustomizado);

        var limitesCustomizados = new FilterLimits { MaxDepth = 2 };
        var ex = Assert.Throws<FilterLimitExceededException>(
            () => FilterLimitValidator.Validate(tree, limitesCustomizados));

        Assert.Equal(FilterLimitKind.MaxDepth, ex.Limit);
        Assert.Equal(2, ex.Allowed);
    }

    [Fact]
    public void Validate_LimitesCustomizados_MaxNodesMenor_RejeitaOQueODefaultAceitaria()
    {
        FilterNode tree = BuildWideGroup(10); // 11 nós — bem abaixo do default de 100.

        var limitesCustomizados = new FilterLimits { MaxNodes = 5 };
        var ex = Assert.Throws<FilterLimitExceededException>(
            () => FilterLimitValidator.Validate(tree, limitesCustomizados));

        Assert.Equal(FilterLimitKind.MaxNodes, ex.Limit);
    }

    [Fact]
    public void Parse_LimitesCustomizados_MaxExpressionLengthMenor_RejeitaExpressaoQueODefaultAceitaria()
    {
        var limitesCustomizados = new FilterLimits { MaxExpressionLength = 5 };

        var ex = Assert.Throws<FilterLimitExceededException>(
            () => FilterExpressionParser.Parse("nome=ana", limitesCustomizados));

        Assert.Equal(FilterLimitKind.MaxExpressionLength, ex.Limit);
    }

    // ---------------------------------------------------------------------------------------
    // Validação acontece antes de qualquer resolução/compilação
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Validate_ArvoreQueExcedeLimiteEReferenciaCampoInexistente_FalhaPeloLimite_NaoPeloCampo()
    {
        // "campo_que_nao_existe*" não é pesquisável em nenhum tipo real — se a validação de
        // limites resolvesse ou tentasse compilar a árvore, falharia por causa do campo. Como o
        // validador nunca olha para PathExtension/mapa de propriedades, o único erro possível é
        // o de limite, mesmo com um campo inexistente presente na árvore.
        FilterNode tree = BuildWideGroup(FilterLimits.Default.MaxNodes + 1, field: "campo_que_nao_existe_");

        var ex = Assert.Throws<FilterLimitExceededException>(() => FilterLimitValidator.Validate(tree));

        Assert.Equal(FilterLimitKind.MaxNodes, ex.Limit);
    }

    [Fact]
    public void Parse_ExpressaoComCampoInexistenteQueExcedeLimite_FalhaPeloLimite_AntesDeQualquerCompilacao()
    {
        // O parser da mini-linguagem nunca resolve nomes de campo (isso é responsabilidade do
        // compilador FilterNode -> Expression, uma etapa posterior e separada). Uma expressão
        // referenciando um campo inexistente, mas cuja árvore excede um limite, precisa falhar
        // pelo limite dentro do próprio Parse — a prova de que a validação ocorre antes de
        // qualquer tentativa de compilar/resolver campos, porque Parse nunca chega a fazer isso.
        string expressao = BuildWideExpression(FilterLimits.Default.MaxNodes + 1, field: "campo_inexistente_");

        var ex = Assert.Throws<FilterLimitExceededException>(() => FilterExpressionParser.Parse(expressao));

        Assert.Equal(FilterLimitKind.MaxNodes, ex.Limit);
    }

    // ---------------------------------------------------------------------------------------
    // Combinação Filter + FilterExpression soma nós para efeito de limite
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ToQuerySpec_FilterEFilterExpressionCadaUmDentroDoLimiteIsoladamente_MasCombinadosExcedem_Lanca()
    {
        // Cada metade tem 56 nós (55 condições + 1 grupo) — dentro do MaxNodes default (100)
        // isoladamente. Combinados por AND (CombineFilters), a árvore final soma 1 (grupo novo) +
        // 56 + 56 = 113 nós, acima do default — só detectável validando a árvore final já
        // combinada, não cada metade isoladamente.
        FilterNode filter = BuildWideGroup(55, field: "a");
        string filterExpression = BuildWideExpression(55, field: "b");

        var request = new RequestQuery { Filter = filter, FilterExpression = filterExpression };

        var ex = Assert.Throws<FilterLimitExceededException>(() => request.ToQuerySpec<Produto>());

        Assert.Equal(FilterLimitKind.MaxNodes, ex.Limit);
    }

    [Fact]
    public void ToQuerySpec_FilterEFilterExpressionCombinados_DentroDoLimite_NaoLanca()
    {
        FilterNode filter = BuildWideGroup(10, field: "a");
        string filterExpression = BuildWideExpression(10, field: "b");

        var request = new RequestQuery { Filter = filter, FilterExpression = filterExpression };

        QuerySpec<Produto> spec = request.ToQuerySpec<Produto>();

        Assert.NotNull(spec.Filter);
        var group = Assert.IsType<FilterGroup>(spec.Filter);
        Assert.Equal(2, group.Children.Count);
    }

    // ---------------------------------------------------------------------------------------
    // FilterLimits.Default e FilterLimitExceededException — sanidade básica
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void FilterLimits_Default_TemOsValoresPadraoDaSecao7DaProposta()
    {
        Assert.Equal(6, FilterLimits.Default.MaxDepth);
        Assert.Equal(100, FilterLimits.Default.MaxNodes);
        Assert.Equal(4096, FilterLimits.Default.MaxExpressionLength);
        Assert.Equal(200, FilterLimits.Default.MaxInItems);
    }

    [Fact]
    public void Validate_RootNulo_LancaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FilterLimitValidator.Validate(null!));
    }
}
