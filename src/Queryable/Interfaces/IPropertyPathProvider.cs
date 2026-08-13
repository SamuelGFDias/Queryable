using System.Reflection;

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
}
