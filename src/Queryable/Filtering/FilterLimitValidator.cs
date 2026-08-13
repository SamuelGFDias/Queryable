namespace Queryable.Filtering;

/// <summary>
/// Percorre uma árvore <see cref="FilterNode"/> já montada e verifica os tetos de
/// <see cref="FilterLimits"/> que dependem da árvore (<see cref="FilterLimits.MaxDepth"/>,
/// <see cref="FilterLimits.MaxNodes"/> e <see cref="FilterLimits.MaxInItems"/>) — o quarto teto,
/// <see cref="FilterLimits.MaxExpressionLength"/>, é verificado sobre a string bruta da
/// mini-linguagem, antes de existir árvore para percorrer (ver
/// <see cref="FilterExpressionParser.Parse(string,FilterLimits?)"/>).
/// </summary>
/// <remarks>
/// <b>Por que a travessia é iterativa, com pilha explícita, em vez de recursiva.</b> O próprio
/// problema que esta classe existe para evitar — uma árvore cliente-controlada profundamente
/// aninhada, usada como vetor de negação de serviço — não pode ser reproduzido pela validação em
/// si: uma implementação recursiva ingênua estouraria a pilha de chamadas exatamente na árvore
/// patológica que deveria ser rejeitada com um erro controlado (<see cref="FilterLimitExceededException"/>),
/// trocando um 400 previsível por um <see cref="StackOverflowException"/> não recuperável no
/// processo. A pilha (<see cref="Stack{T}"/>) mantida aqui vive no heap, não na pilha de
/// chamadas, então seu tamanho não é limitado pelo espaço de pilha do thread.
/// </remarks>
public static class FilterLimitValidator
{
    /// <summary>
    /// Valida <paramref name="root"/> contra <paramref name="limits"/> (ou
    /// <see cref="FilterLimits.Default"/>, quando <c>null</c>). Não faz nada quando a árvore está
    /// dentro de todos os tetos; lança <see cref="FilterLimitExceededException"/> assim que
    /// encontra o primeiro nó que viola algum deles — a ordem de detecção entre os diferentes
    /// tetos não é garantida (depende da ordem de travessia), mas o primeiro problema encontrado
    /// é sempre reportado com precisão (o tipo de teto, o valor encontrado e o permitido).
    /// </summary>
    /// <exception cref="ArgumentNullException">Quando <paramref name="root"/> é <c>null</c>.</exception>
    /// <exception cref="FilterLimitExceededException">
    /// Quando a profundidade de aninhamento, o número total de nós, ou a quantidade de itens de
    /// alguma lista do operador <c>in</c> excede o respectivo teto.
    /// </exception>
    public static void Validate(FilterNode root, FilterLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        limits ??= FilterLimits.Default;

        var pending = new Stack<(FilterNode Node, int Depth)>();
        pending.Push((root, 1));

        int nodeCount = 0;

        while (pending.Count > 0)
        {
            (FilterNode node, int depth) = pending.Pop();

            nodeCount++;
            if (nodeCount > limits.MaxNodes)
                throw new FilterLimitExceededException(
                    FilterLimitKind.MaxNodes,
                    nodeCount,
                    limits.MaxNodes,
                    $"Árvore de filtro excede o número máximo de nós permitido: encontrado " +
                    $"(pelo menos) {nodeCount}, permitido {limits.MaxNodes}.");

            if (depth > limits.MaxDepth)
                throw new FilterLimitExceededException(
                    FilterLimitKind.MaxDepth,
                    depth,
                    limits.MaxDepth,
                    $"Árvore de filtro excede a profundidade máxima de aninhamento permitida: " +
                    $"encontrado {depth}, permitido {limits.MaxDepth}.");

            switch (node)
            {
                case FilterCondition condition:
                    ValidateInItems(condition, limits);
                    break;

                case FilterGroup group:
                    foreach (FilterNode child in group.Children)
                        pending.Push((child, depth + 1));
                    break;

                case FilterNot not:
                    pending.Push((not.Inner, depth + 1));
                    break;

                default:
                    throw new NotSupportedException($"Tipo de nó '{node.GetType().Name}' não suportado.");
            }
        }
    }

    /// <summary>
    /// Conta os itens da lista CSV de uma <see cref="FilterCondition"/> com
    /// <c>Operator == "in"</c> (comparação exata, mesmo critério usado pelo compilador em
    /// <c>Queryable.Builders.FilterBuilder</c> para reconhecer o operador) e valida contra
    /// <see cref="FilterLimits.MaxInItems"/>.
    /// </summary>
    private static void ValidateInItems(FilterCondition condition, FilterLimits limits)
    {
        if (condition.Operator != "in")
            return;

        int itemCount = string.IsNullOrEmpty(condition.Value)
            ? 0
            : condition.Value.Split(',').Length;

        if (itemCount > limits.MaxInItems)
            throw new FilterLimitExceededException(
                FilterLimitKind.MaxInItems,
                itemCount,
                limits.MaxInItems,
                $"Lista do operador 'in' no campo '{condition.Field}' excede a quantidade " +
                $"máxima de itens permitida: encontrado {itemCount}, permitido {limits.MaxInItems}.");
    }
}
