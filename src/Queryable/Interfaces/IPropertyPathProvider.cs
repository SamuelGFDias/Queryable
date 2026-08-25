using System.Linq.Expressions;
using System.Reflection;
using Queryable.Configuration;

namespace Queryable.Interfaces;

/// <summary>
/// Resolve, para um tipo <typeparamref name="T"/>, o mapa de alias para a cadeia de
/// <see cref="PropertyInfo"/> correspondente, usado por <c>FilterBuilder</c> e
/// <c>SortBuilder</c> para encadear <c>Expression.Property</c> a partir de um nome de campo
/// vindo da query string.
/// </summary>
/// <remarks>
/// A busca por chave no dicionário devolvido é case-insensitive (mesma garantia de
/// <c>PathExtension.BuildPropertyPaths&lt;T&gt;()</c>, de onde o mapa é originado) — aliases como
/// <c>"categoria.nome"</c>, <c>"Categoria.Nome"</c> e <c>"CATEGORIA.NOME"</c> resolvem para a
/// mesma entrada.
/// </remarks>
public interface IPropertyPathProvider
{
    /// <summary>
    /// Devolve o mapa de alias (case-insensitive) para a cadeia de propriedades de <typeparamref name="T"/>.
    /// </summary>
    IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths<T>();

    /// <summary>
    /// Devolve o mapa de alias (case-insensitive) para a cadeia de propriedades de <typeparamref name="T"/>,
    /// restrito aos aliases disponíveis para o uso pedido (<see cref="QueryableUsage.Filter"/> ou
    /// <see cref="QueryableUsage.Sort"/>) — ver <c>QueryablePropertyBuilder{TEntity}.FilterOnly</c>/
    /// <c>SortOnly</c>.
    /// </summary>
    /// <remarks>
    /// Método de interface padrão, aditivo: implementações externas de <see cref="IPropertyPathProvider"/>
    /// escritas antes desta sobrecarga existir continuam compilando e funcionando — a implementação
    /// padrão delega para <see cref="GetPaths{T}()"/>, ignorando <paramref name="usage"/> (mesmo
    /// comportamento de antes desta sobrecarga existir). <c>ReflectionPropertyPathProvider</c>
    /// sobrescreve com a resolução real por uso.
    /// </remarks>
    IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths<T>(QueryableUsage usage) => GetPaths<T>();

    /// <summary>
    /// Devolve o mapa de alias (case-insensitive) para expressão de ordenação composta de
    /// <typeparamref name="T"/>, configuradas via <c>QueryableConfiguration{TEntity}.ForSort(...).As(...)</c>.
    /// </summary>
    /// <remarks>
    /// Método de interface padrão, aditivo, pelo mesmo motivo de
    /// <see cref="GetPaths{T}(QueryableUsage)"/>: a implementação padrão devolve um dicionário
    /// vazio compartilhado, então uma implementação externa de <see cref="IPropertyPathProvider"/>
    /// escrita antes desta sobrecarga existir continua funcionando (nenhuma expressão composta é
    /// resolvida, exatamente como hoje). <c>ReflectionPropertyPathProvider</c> sobrescreve com as
    /// expressões vindas da configuração fluente.
    /// </remarks>
    IReadOnlyDictionary<string, LambdaExpression> GetSortExpressions<T>() =>
        EmptySortExpressions;

    private static readonly IReadOnlyDictionary<string, LambdaExpression> EmptySortExpressions =
        new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase);
}
