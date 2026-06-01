using Api.Context;
using Api.Interfaces;
using Api.Models;

namespace Api.Repositories;

public class ProdutoRepository(AppDbContext context) : IProdutoRepository
{
    public IQueryable<Produto> GetQueryable()
    {
        return context.Set<Produto>();
    }

    public async Task Create(Produto produto)
    {
        await context.Set<Produto>().AddAsync(produto);
        await context.SaveChangesAsync();
    }
}