using System.Linq.Expressions;
using Queryable.Filtering;

namespace Queryable.Interfaces;

public interface IFilterBuilder
{
    Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams);

    /// <summary>
    /// Compila uma árvore de filtro composto (<see cref="FilterNode"/> — condições combinadas
    /// por <c>AND</c>/<c>OR</c>, com suporte a negação e aninhamento) em uma expressão de
    /// predicado.
    /// </summary>
    /// <remarks>
    /// Default interface method (C# 8+): implementações externas de <see cref="IFilterBuilder"/>
    /// escritas antes desta árvore existir continuam compilando sem alteração, porque este
    /// membro tem corpo padrão e não é obrigatório de sobrescrever. Quem não sobrescrever recebe
    /// <see cref="NotSupportedException"/> ao tentar compilar uma árvore composta. A
    /// implementação concreta fornecida pela própria biblioteca
    /// (<see cref="Queryable.Builders.FilterBuilder"/>) sobrescreve este método com a compilação
    /// real.
    /// </remarks>
    Expression<Func<T, bool>> BuildPredicate<T>(FilterNode filter)
        => throw new NotSupportedException("Implementação de IFilterBuilder não suporta árvore de filtro composta.");
}