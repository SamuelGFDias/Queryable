using System.Linq.Expressions;
using Queryable.Core;

namespace Queryable.EntityFrameworkCore.Interfaces;

/// <summary>
/// Aplica filtro, ordenação e paginação dinâmicos (via <see cref="QuerySpec{T}"/>)
/// sobre um <see cref="IQueryable{T}"/> do Entity Framework Core e projeta o
/// resultado diretamente para um DTO, retornando um <see cref="PagedResult{T}"/>.
/// </summary>
public interface IPagedQueryService
{
    /// <summary>
    /// Aplica o <see cref="RequestQuery"/> (convertido para <see cref="QuerySpec{T}"/>) sobre
    /// <paramref name="query"/>, projeta o resultado paginado usando <paramref name="projection"/>
    /// e retorna um <see cref="PagedResult{TDto}"/>.
    /// </summary>
    /// <remarks>
    /// A consulta é executada com rastreamento desabilitado (<c>AsNoTracking</c>) e a
    /// contagem total é omitida quando <see cref="RequestQuery.SkipTotalCount"/> é <c>true</c>.
    /// <para>
    /// Cuidado ao chamar esta sobrecarga passando <paramref name="afterSpec"/> posicionalmente
    /// como terceiro argumento junto com o literal <c>null</c>: como existe uma segunda
    /// sobrecarga com a mesma aridade (a que usa <see cref="IProjectable{TEntity, TSelf}"/>),
    /// o compilador pode não conseguir escolher entre elas. Nesse caso, use o argumento
    /// nomeado <c>afterSpec:</c> para desambiguar.
    /// </para>
    /// <para>
    /// <b><paramref name="afterSpec"/> roda depois do filtro/ordenação de <paramref name="request"/>
    /// e antes da contagem e da paginação</b> — nessa ordem: filtro/ordenação → <paramref name="afterSpec"/>
    /// → <c>CountAsync</c> → <c>Skip</c>/<c>Take</c>. Qualquer <c>Where</c> aplicado dentro de
    /// <paramref name="afterSpec"/> também é contado, ou seja, ele afeta
    /// <see cref="PagedResult{T}.Meta"/>.<c>TotalCount</c>, não só os itens da página.
    /// </para>
    /// <para>
    /// <b>Ordenação aplicada a <paramref name="query"/> antes desta chamada é descartada.</b>
    /// A ordenação de <paramref name="request"/> é sempre (re)aplicada internamente via
    /// <c>OrderBy</c> — inclusive quando <see cref="RequestQuery.Sort"/> está vazio, caso em que
    /// o fallback é <c>OrderBy(x =&gt; 0)</c> — e <c>OrderBy</c> substitui qualquer ordenação
    /// anterior da <see cref="IQueryable{T}"/> em vez de compor com ela (isso só aconteceria com
    /// <c>ThenBy</c>). Um <c>query.OrderBy(...)</c> feito pelo chamador antes de passar
    /// <paramref name="query"/> para este método é, portanto, sempre perdido.
    /// </para>
    /// <para>
    /// Por causa disso, uma ordenação padrão (quando o cliente não pediu <c>sort</c>) precisa
    /// ser aplicada dentro de <paramref name="afterSpec"/>, nunca em <paramref name="query"/>. E
    /// precisa ser <b>condicional</b>: um <c>OrderBy</c> incondicional dentro de
    /// <paramref name="afterSpec"/> também substitui — pelo mesmo motivo — a ordenação que o
    /// cliente pediu via <see cref="RequestQuery.Sort"/>, silenciosamente. Exemplo do padrão
    /// correto:
    /// <code>
    /// afterSpec: q =&gt; string.IsNullOrWhiteSpace(request.Sort)
    ///     ? q.OrderByDescending(p =&gt; p.CriadoEm)
    ///     : q
    /// </code>
    /// </para>
    /// </remarks>
    /// <typeparam name="TEntity">Tipo da entidade de origem da consulta.</typeparam>
    /// <typeparam name="TDto">Tipo do DTO de destino da projeção.</typeparam>
    /// <param name="query">Consulta base sobre a entidade.</param>
    /// <param name="request">Requisição achatada com filtro, ordenação e paginação.</param>
    /// <param name="projection">Expressão de projeção de <typeparamref name="TEntity"/> para <typeparamref name="TDto"/>.</param>
    /// <param name="afterSpec">
    /// Transformação opcional aplicada após o filtro/ordenação e antes da contagem e da
    /// paginação (ex.: <c>Include</c> adicionais, filtros de segurança não expressáveis via
    /// <see cref="QuerySpec{T}"/>, ordenação padrão condicional — ver <c>remarks</c>).
    /// </param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Página de <typeparamref name="TDto"/> com os metadados de paginação.</returns>
    /// <exception cref="ArgumentNullException">Quando <paramref name="query"/>, <paramref name="request"/> ou <paramref name="projection"/> é <c>null</c>.</exception>
    Task<PagedResult<TDto>> ApplyFilterPaginatedAsync<TEntity, TDto>(
        IQueryable<TEntity> query,
        RequestQuery request,
        Expression<Func<TEntity, TDto>> projection,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? afterSpec = null,
        CancellationToken ct = default)
        where TEntity : class
        where TDto : class;

    /// <summary>
    /// Igual a <see cref="ApplyFilterPaginatedAsync{TEntity, TDto}(IQueryable{TEntity}, RequestQuery, Expression{Func{TEntity, TDto}}, Func{IQueryable{TEntity}, IQueryable{TEntity}}?, CancellationToken)"/>,
    /// mas obtém a expressão de projeção diretamente de <c>TDto.Projection</c>
    /// (<see cref="IProjectable{TEntity, TSelf}"/>), dispensando que o chamador a informe.
    /// </summary>
    /// <remarks>
    /// Cuidado ao chamar esta sobrecarga passando <paramref name="afterSpec"/> posicionalmente
    /// como terceiro argumento junto com o literal <c>null</c>: como existe uma segunda
    /// sobrecarga com a mesma aridade (a que recebe <c>projection</c> explícita), o
    /// compilador pode não conseguir escolher entre elas. Nesse caso, use o argumento
    /// nomeado <c>afterSpec:</c> para desambiguar.
    /// <para>
    /// <b><paramref name="afterSpec"/> roda depois do filtro/ordenação de <paramref name="request"/>
    /// e antes da contagem e da paginação</b> — nessa ordem: filtro/ordenação → <paramref name="afterSpec"/>
    /// → <c>CountAsync</c> → <c>Skip</c>/<c>Take</c>. Qualquer <c>Where</c> aplicado dentro de
    /// <paramref name="afterSpec"/> também é contado, ou seja, ele afeta
    /// <see cref="PagedResult{T}.Meta"/>.<c>TotalCount</c>, não só os itens da página.
    /// </para>
    /// <para>
    /// <b>Ordenação aplicada a <paramref name="query"/> antes desta chamada é descartada.</b>
    /// A ordenação de <paramref name="request"/> é sempre (re)aplicada internamente via
    /// <c>OrderBy</c> — inclusive quando <see cref="RequestQuery.Sort"/> está vazio, caso em que
    /// o fallback é <c>OrderBy(x =&gt; 0)</c> — e <c>OrderBy</c> substitui qualquer ordenação
    /// anterior da <see cref="IQueryable{T}"/> em vez de compor com ela (isso só aconteceria com
    /// <c>ThenBy</c>). Um <c>query.OrderBy(...)</c> feito pelo chamador antes de passar
    /// <paramref name="query"/> para este método é, portanto, sempre perdido.
    /// </para>
    /// <para>
    /// Por causa disso, uma ordenação padrão (quando o cliente não pediu <c>sort</c>) precisa
    /// ser aplicada dentro de <paramref name="afterSpec"/>, nunca em <paramref name="query"/>. E
    /// precisa ser <b>condicional</b>: um <c>OrderBy</c> incondicional dentro de
    /// <paramref name="afterSpec"/> também substitui — pelo mesmo motivo — a ordenação que o
    /// cliente pediu via <see cref="RequestQuery.Sort"/>, silenciosamente. Exemplo do padrão
    /// correto:
    /// <code>
    /// afterSpec: q =&gt; string.IsNullOrWhiteSpace(request.Sort)
    ///     ? q.OrderByDescending(p =&gt; p.CriadoEm)
    ///     : q
    /// </code>
    /// </para>
    /// </remarks>
    /// <typeparam name="TEntity">Tipo da entidade de origem da consulta.</typeparam>
    /// <typeparam name="TDto">
    /// Tipo do DTO de destino da projeção. Deve implementar <see cref="IProjectable{TEntity, TSelf}"/>
    /// para <typeparamref name="TEntity"/>.
    /// </typeparam>
    /// <param name="query">Consulta base sobre a entidade.</param>
    /// <param name="request">Requisição achatada com filtro, ordenação e paginação.</param>
    /// <param name="afterSpec">
    /// Transformação opcional aplicada após o filtro/ordenação e antes da contagem e da
    /// paginação (ex.: <c>Include</c> adicionais, filtros de segurança não expressáveis via
    /// <see cref="QuerySpec{T}"/>, ordenação padrão condicional — ver <c>remarks</c>).
    /// </param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Página de <typeparamref name="TDto"/> com os metadados de paginação.</returns>
    /// <exception cref="ArgumentNullException">Quando <paramref name="query"/> ou <paramref name="request"/> é <c>null</c>.</exception>
    Task<PagedResult<TDto>> ApplyFilterPaginatedAsync<TEntity, TDto>(
        IQueryable<TEntity> query,
        RequestQuery request,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? afterSpec = null,
        CancellationToken ct = default)
        where TEntity : class
        where TDto : class, IProjectable<TEntity, TDto>;
}
