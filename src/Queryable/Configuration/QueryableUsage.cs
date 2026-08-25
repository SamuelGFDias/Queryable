namespace Queryable.Configuration;

/// <summary>
/// Indica em qual(is) contexto(s) um alias configurado via <see cref="QueryableConfiguration{TEntity}.For{TProperty}"/>
/// fica disponível: apenas filtro, apenas ordenação, ou ambos (padrão).
/// </summary>
/// <remarks>
/// Consumido por <c>ReflectionPropertyPathProvider.BuildFinalMap{T}</c> ao compor o mapa final por
/// uso pedido (<see cref="Interfaces.IPropertyPathProvider.GetPaths{T}(QueryableUsage)"/>): um alias
/// só entra no mapa de <see cref="Filter"/> se <c>(Uso &amp; QueryableUsage.Filter) == QueryableUsage.Filter</c>,
/// e da mesma forma para <see cref="Sort"/>. Como <see cref="All"/> combina os dois bits, um alias
/// declarado com <see cref="Filter"/> ou <see cref="Sort"/> isoladamente <b>não</b> aparece no mapa
/// pedido com <see cref="All"/> — esse mapa é reservado para aliases "seguros nos dois usos"
/// (automáticos por reflexão, ou explicitamente <see cref="All"/>).
/// </remarks>
[Flags]
public enum QueryableUsage
{
    /// <summary>O alias só é resolvido por <c>FilterBuilder</c> — não aparece para ordenação.</summary>
    Filter = 1,

    /// <summary>O alias só é resolvido por <c>SortBuilder</c> — não aparece para filtro.</summary>
    Sort = 2,

    /// <summary>O alias é resolvido tanto por filtro quanto por ordenação (comportamento padrão de <c>For(...)</c>).</summary>
    All = Filter | Sort
}
