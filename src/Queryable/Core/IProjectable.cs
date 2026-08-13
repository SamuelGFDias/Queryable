using System.Linq.Expressions;

namespace Queryable.Core;

/// <summary>
/// Permite que um DTO declare a própria projeção a partir de uma entidade.
/// A ideia é inverter a responsabilidade: em vez de um serviço genérico usar
/// reflexão (<c>Activator.CreateInstance</c>, varredura de <c>GetTypes()</c>, etc.)
/// para descobrir "como mapear" cada entidade para o DTO desejado, o próprio DTO
/// expõe sua expressão de projeção como membro estático, resolvido em tempo de
/// compilação através de <c>static abstract</c> (C# 11+). Não há reflexão em
/// tempo de execução envolvida na resolução do mapeamento.
/// </summary>
/// <remarks>
/// Por ser uma <see cref="Expression{TDelegate}"/> (e não um <c>Func</c> já compilado),
/// a projeção pode ser passada para um <c>IQueryable.Select</c> e traduzida pelo
/// provider (ex.: Entity Framework Core) diretamente para a consulta SQL — apenas
/// as colunas usadas pelo DTO trafegam do banco de dados, sem materializar a
/// entidade inteira antes de mapear.
/// </remarks>
/// <typeparam name="TEntity">Tipo da entidade de origem da projeção.</typeparam>
/// <typeparam name="TSelf">O próprio tipo que implementa a interface (CRTP).</typeparam>
/// <example>
/// <code>
/// public class PedidoDto : IProjectable&lt;Pedido, PedidoDto&gt;
/// {
///     public int Id { get; set; }
///     public string Numero { get; set; } = string.Empty;
///
///     public static Expression&lt;Func&lt;Pedido, PedidoDto&gt;&gt; Projection =&gt;
///         pedido =&gt; new PedidoDto
///         {
///             Id = pedido.Id,
///             Numero = pedido.Numero
///         };
/// }
/// </code>
/// </example>
public interface IProjectable<TEntity, TSelf>
    where TSelf : class, IProjectable<TEntity, TSelf>
{
    /// <summary>
    /// Expressão que projeta <typeparamref name="TEntity"/> em <typeparamref name="TSelf"/>.
    /// Implementada como membro estático abstrato: cada DTO fornece a própria
    /// expressão, resolvida em tempo de compilação, sem necessidade de reflexão
    /// ou de um mapper centralizado descoberto em tempo de execução.
    /// </summary>
    static abstract Expression<Func<TEntity, TSelf>> Projection { get; }
}
