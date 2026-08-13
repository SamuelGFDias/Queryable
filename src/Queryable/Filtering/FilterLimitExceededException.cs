namespace Queryable.Filtering;

/// <summary>
/// Identifica qual dos quatro tetos de <see cref="FilterLimits"/> foi violado, para o chamador
/// de <see cref="FilterLimitExceededException"/> poder tratar cada caso sem parsear a mensagem.
/// </summary>
public enum FilterLimitKind
{
    /// <summary>Ver <see cref="FilterLimits.MaxDepth"/>.</summary>
    MaxDepth,

    /// <summary>Ver <see cref="FilterLimits.MaxNodes"/>.</summary>
    MaxNodes,

    /// <summary>Ver <see cref="FilterLimits.MaxExpressionLength"/>.</summary>
    MaxExpressionLength,

    /// <summary>Ver <see cref="FilterLimits.MaxInItems"/>.</summary>
    MaxInItems
}

/// <summary>
/// Lançada quando uma árvore de filtro composto (<see cref="FilterNode"/>), ou a string de
/// entrada da mini-linguagem que a origina, excede um dos tetos de <see cref="FilterLimits"/>.
/// Especialização de <see cref="ArgumentException"/> — qualquer código que já captura
/// <see cref="ArgumentException"/> continua funcionando sem alteração.
/// </summary>
/// <remarks>
/// Lançada por <see cref="FilterLimitValidator"/> (limites <see cref="FilterLimitKind.MaxDepth"/>,
/// <see cref="FilterLimitKind.MaxNodes"/> e <see cref="FilterLimitKind.MaxInItems"/>, que exigem
/// percorrer a árvore já montada) e por
/// <see cref="FilterExpressionParser.Parse(string,FilterLimits?)"/> (limite
/// <see cref="FilterLimitKind.MaxExpressionLength"/>, verificado sobre a string bruta, antes de
/// existir qualquer <see cref="FilterNode"/> para percorrer).
/// </remarks>
public sealed class FilterLimitExceededException : ArgumentException
{
    /// <summary>Qual dos tetos de <see cref="FilterLimits"/> foi violado.</summary>
    public FilterLimitKind Limit { get; }

    /// <summary>Valor efetivamente encontrado (profundidade, contagem de nós, de itens, ou de caracteres).</summary>
    public int Found { get; }

    /// <summary>Valor máximo permitido, conforme configurado em <see cref="FilterLimits"/>.</summary>
    public int Allowed { get; }

    public FilterLimitExceededException(FilterLimitKind limit, int found, int allowed, string message)
        : base(message)
    {
        Limit = limit;
        Found = found;
        Allowed = allowed;
    }
}
