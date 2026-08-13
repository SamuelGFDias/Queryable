namespace Queryable.Filtering;

/// <summary>
/// Erro de sintaxe ao interpretar uma expressão da mini-linguagem textual de filtros compostos
/// (<see cref="FilterExpressionParser"/>). Especialização de <see cref="ArgumentException"/> —
/// qualquer código que já captura <see cref="ArgumentException"/> continua funcionando sem
/// alteração. <see cref="Position"/> dá a posição aproximada (1-based, em número de caracteres)
/// na string de entrada onde o parser detectou o problema, para mensagens de erro mais úteis ao
/// chamador do que um <see cref="IndexOutOfRangeException"/> ou uma mensagem genérica.
/// </summary>
public sealed class FilterExpressionSyntaxException : ArgumentException
{
    /// <summary>
    /// Posição aproximada (1-based) na string de entrada onde o erro foi detectado.
    /// </summary>
    public int Position { get; }

    public FilterExpressionSyntaxException(string message, int position)
        : base($"{message} (posição {position}).")
    {
        Position = position;
    }
}
