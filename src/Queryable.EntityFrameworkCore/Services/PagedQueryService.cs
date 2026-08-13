using System.Linq.Expressions;
using global::Microsoft.EntityFrameworkCore;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Queryable.EntityFrameworkCore.Services;

/// <summary>
/// Implementação padrão de <see cref="IPagedQueryService"/>, baseada em
/// <see cref="IQuerySpecApplier"/> para aplicar filtro/ordenação/paginação e em
/// Entity Framework Core para materializar a página já projetada para o DTO.
/// </summary>
/// <param name="querySpecApplier">Serviço do núcleo responsável por aplicar o <see cref="QuerySpec{T}"/>.</param>
public class PagedQueryService(IQuerySpecApplier querySpecApplier) : IPagedQueryService
{
    /// <inheritdoc />
    public async Task<PagedResult<TDto>> ApplyFilterPaginatedAsync<TEntity, TDto>(
        IQueryable<TEntity> query,
        RequestQuery request,
        Expression<Func<TEntity, TDto>> projection,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? afterSpec = null,
        CancellationToken ct = default)
        where TEntity : class
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projection);

        QuerySpec<TEntity> spec = request.ToQuerySpec<TEntity>();

        IQueryable<TEntity> filtered = querySpecApplier.Apply(query.AsNoTracking(), spec);

        if (afterSpec is not null)
            filtered = afterSpec(filtered);

        int totalCount = spec.SkipTotalCount ? 0 : await filtered.CountAsync(ct);

        List<TDto> items = await querySpecApplier.ApplyPaged(filtered, spec)
            .Select(projection)
            .ToListAsync(ct);

        return items.ToPagedResult(spec.Page, spec.PageSize, totalCount);
    }

    /// <inheritdoc />
    public Task<PagedResult<TDto>> ApplyFilterPaginatedAsync<TEntity, TDto>(
        IQueryable<TEntity> query,
        RequestQuery request,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? afterSpec = null,
        CancellationToken ct = default)
        where TEntity : class
        where TDto : class, IProjectable<TEntity, TDto>
    {
        return ApplyFilterPaginatedAsync(query, request, TDto.Projection, afterSpec, ct);
    }
}
