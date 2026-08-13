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
    /// </remarks>
    /// <typeparam name="TEntity">Tipo da entidade de origem da consulta.</typeparam>
    /// <typeparam name="TDto">Tipo do DTO de destino da projeção.</typeparam>
    /// <param name="query">Consulta base sobre a entidade.</param>
    /// <param name="request">Requisição achatada com filtro, ordenação e paginação.</param>
    /// <param name="projection">Expressão de projeção de <typeparamref name="TEntity"/> para <typeparamref name="TDto"/>.</param>
    /// <param name="afterSpec">
    /// Transformação opcional aplicada após o filtro/ordenação e antes da paginação
    /// (ex.: <c>Include</c> adicionais, filtros de segurança não expressáveis via <see cref="QuerySpec{T}"/>).
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
    /// </remarks>
    /// <typeparam name="TEntity">Tipo da entidade de origem da consulta.</typeparam>
    /// <typeparam name="TDto">
    /// Tipo do DTO de destino da projeção. Deve implementar <see cref="IProjectable{TEntity, TSelf}"/>
    /// para <typeparamref name="TEntity"/>.
    /// </typeparam>
    /// <param name="query">Consulta base sobre a entidade.</param>
    /// <param name="request">Requisição achatada com filtro, ordenação e paginação.</param>
    /// <param name="afterSpec">
    /// Transformação opcional aplicada após o filtro/ordenação e antes da paginação
    /// (ex.: <c>Include</c> adicionais, filtros de segurança não expressáveis via <see cref="QuerySpec{T}"/>).
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
