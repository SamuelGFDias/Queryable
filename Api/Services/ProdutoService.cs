using Api.Interfaces;
using Api.Models;
using Api.Repositories;
using Queryable.Core;
using Queryable.Interfaces;

namespace Api.Services;

public class ProdutoService(IProdutoRepository repository, IQuerySpecApplier querySpecApplier)
    : IProdutoService
{
    public PagedResult<Produto> Buscar(QuerySpec<Produto> spec)
    {
        IQueryable<Produto> query = repository.GetQueryable();
        return querySpecApplier.Apply(query, spec);
    }

    public async Task Criar(Produto produto)
    {
        await repository.Create(produto);
    }
}