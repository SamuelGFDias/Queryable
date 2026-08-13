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

    /// <summary>
    /// Combina o dicionário legado <c>campo__operador=valor</c> com uma árvore de filtro
    /// composto opcional, produzindo um único predicado.
    /// </summary>
    /// <remarks>
    /// Default interface method (C# 8+), pelo mesmo motivo de
    /// <see cref="BuildPredicate{T}(FilterNode)"/>: aditivo, não quebra implementações externas
    /// de <see cref="IFilterBuilder"/> escritas antes desta sobrecarga existir.
    /// <para>
    /// Regra de combinação: se <paramref name="filter"/> for <c>null</c>, o comportamento é
    /// idêntico ao de <see cref="BuildPredicate{T}(IDictionary{string,string})"/> — só
    /// <paramref name="queryParams"/> é considerado. Se <paramref name="filter"/> estiver
    /// preenchido, a implementação padrão aqui não sabe combinar os dois formatos e lança
    /// <see cref="NotSupportedException"/>; a implementação concreta fornecida pela própria
    /// biblioteca (<see cref="Queryable.Builders.FilterBuilder"/>) sobrescreve este método
    /// combinando os dois conjuntos de condições por <c>AND</c>.
    /// </para>
    /// </remarks>
    Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams, FilterNode? filter)
        => filter is null
            ? BuildPredicate<T>(queryParams)
            : throw new NotSupportedException(
                "Implementação de IFilterBuilder não suporta combinar o dicionário legado com uma árvore de filtro composta.");
}