namespace Queryable.Filtering;

/// <summary>
/// Tetos de segurança aplicados a uma requisição vinda de cliente externo — sobretudo à árvore de
/// filtro composto (<see cref="FilterNode"/>) vinda
/// de um cliente externo — porta JSON (<see cref="FilterNodeJsonConverter"/>) ou mini-linguagem
/// de query string (<see cref="FilterExpressionParser"/>) —, validados por
/// <see cref="FilterLimitValidator"/> antes de qualquer compilação para
/// <see cref="System.Linq.Expressions.Expression"/>.
/// </summary>
/// <remarks>
/// <para>
/// Motivação (seção 7 de <c>docs/proposta-filtros-compostos.md</c>): aceitar uma expressão
/// booleana arbitrária vinda de um chamador externo é, por natureza, aceitar um pequeno
/// "programa" a ser executado pelo servidor. Sem teto, uma árvore profundamente aninhada ou com
/// um número enorme de nós força o compilador a montar uma <c>Expression</c> gigantesca e o
/// banco a planejar um <c>WHERE</c> patológico — um vetor de negação de serviço tanto na camada
/// de aplicação (tempo/memória de montagem) quanto no banco.
/// </para>
/// <para>
/// Todas as propriedades são mutáveis e têm os defaults propostos na seção 7. Registre uma
/// instância customizada via
/// <see cref="Queryable.Extensions.ServiceCollectionExtensions.AddQueryableDynamicFilter(Microsoft.Extensions.DependencyInjection.IServiceCollection,Action{FilterLimits})"/>
/// para sobrescrever os defaults por aplicação.
/// </para>
/// </remarks>
public sealed class FilterLimits
{
    /// <summary>
    /// Profundidade máxima de aninhamento de <see cref="FilterGroup"/>/<see cref="FilterNot"/>
    /// na árvore. Default <c>6</c> — acompanha a mesma ordem de grandeza do <c>MaxDepth = 5</c>
    /// já usado em <c>PathExtension</c> para navegação de propriedades.
    /// </summary>
    public int MaxDepth { get; set; } = 6;

    /// <summary>
    /// Número total de nós (<see cref="FilterCondition"/> + <see cref="FilterGroup"/> +
    /// <see cref="FilterNot"/>, somados recursivamente) permitido na árvore. Default <c>100</c>
    /// — teto generoso para filtros legítimos, baixo o suficiente para impedir árvores de
    /// milhares de nós.
    /// </summary>
    public int MaxNodes { get; set; } = 100;

    /// <summary>
    /// Tamanho máximo, em caracteres, da string de entrada da mini-linguagem
    /// (<see cref="FilterExpressionParser"/>), verificado antes de tokenizar. Default
    /// <c>4096</c> — evita gastar tempo de parsing em payloads absurdamente longos antes mesmo
    /// de contar nós.
    /// </summary>
    public int MaxExpressionLength { get; set; } = 4096;

    /// <summary>
    /// Quantidade máxima de itens na lista CSV de uma <see cref="FilterCondition"/> com
    /// <c>Operator == "in"</c>. Default <c>200</c> — mesma motivação do teto de nós, aplicada à
    /// explosão horizontal de uma única condição.
    /// </summary>
    public int MaxInItems { get; set; } = 200;

    /// <summary>
    /// Quantidade máxima de itens por página (<c>QuerySpec&lt;T&gt;.PageSize</c>). Default
    /// <c>1000</c>. Os demais tetos protegem a <i>complexidade</i> do filtro; este protege o
    /// <i>volume</i> da resposta — sem ele, um cliente pode pedir <c>pageSize=1000000</c> e a
    /// biblioteca aceita, deixando o teto inteiramente por conta da aplicação. Verificado por
    /// <see cref="FilterLimitValidator.ValidatePageSize"/> nos mesmos pontos que revalidam a
    /// árvore de filtro (<c>QuerySpecModelBinder&lt;T&gt;.BuildSpec</c>,
    /// <c>RequestQueryExtensions.ToQuerySpec&lt;T&gt;</c> e, por consequência,
    /// <c>PagedQueryService</c>). Valores de <c>PageSize</c> menores ou iguais a zero nunca chegam
    /// aqui: o setter de <c>QuerySpec&lt;T&gt;.PageSize</c> os descarta e mantém o valor anterior.
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// Instância padrão compartilhada (todos os valores nos defaults acima), usada como
    /// <b>fallback</b> por código estático que não tem acesso ao container de DI —
    /// <see cref="FilterExpressionParser.Parse(string,FilterLimits?)"/> e
    /// <see cref="FilterNodeJsonConverter"/> não recebem <see cref="IServiceProvider"/> (o
    /// primeiro é um método estático puro; o segundo é um
    /// <see cref="System.Text.Json.Serialization.JsonConverter{T}"/>, que o
    /// <c>System.Text.Json</c> instancia sem passar contexto de DI) — quando nenhuma instância
    /// explícita é fornecida pelo chamador.
    /// </summary>
    /// <remarks>
    /// Decisão deliberada: esta instância nunca é mutada pela configuração via DI
    /// (<c>AddQueryableDynamicFilter(Action&lt;FilterLimits&gt;)</c> sempre cria uma
    /// <see cref="FilterLimits"/> nova e separada para registrar no container). Consequência
    /// prática: código com acesso ao container (ex.:
    /// <c>QuerySpecModelBinder&lt;T&gt;.BindModelAsync</c> via <c>HttpContext.RequestServices</c>,
    /// ou <c>PagedQueryService</c> do pacote EF Core, que recebe <see cref="FilterLimits"/> por
    /// injeção de construtor) resolve e aplica os limites configurados normalmente; a porta JSON
    /// (<see cref="FilterNodeJsonConverter"/>) valida contra estes defaults <b>a menos que</b> a
    /// aplicação registre um conversor construído com os limites configurados nas
    /// <see cref="System.Text.Json.JsonSerializerOptions"/> — ver
    /// <see cref="Queryable.Extensions.JsonSerializerOptionsExtensions.AddQueryableFilterLimits"/>.
    /// </remarks>
    public static FilterLimits Default { get; } = new();
}
