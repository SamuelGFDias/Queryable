using Api.Dtos;
using Api.Interfaces;
using Api.Models;
using Microsoft.EntityFrameworkCore;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Interfaces;

namespace Api.Services;

public class ProdutoService(IProdutoRepository repository, IQuerySpecApplier querySpecApplier)
    : IProdutoService
{
    public async Task<PagedResult<ProdutoDto>> Buscar(QuerySpec<Produto> spec)
    {
        IQueryable<Produto> query = repository.GetQueryable();
        IQueryable<Produto> filtered = querySpecApplier.Apply(query, spec);
        
        int totalCount = await filtered.CountAsync();

        IQueryable<Produto> paged = querySpecApplier.ApplyPaged(filtered, spec);

        List<ProdutoDto> dto = await paged
                                    .Select(x => (ProdutoDto)x)
                                    .ToListAsync();

        return dto.ToPagedResult(spec.Page, spec.PageSize, totalCount);
    }

    public async Task Criar(CriarProduto produto)
    {
        await repository.Create((Produto)produto);
    }
}