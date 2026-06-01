using Api.Models;

namespace Api.Interfaces;

public interface IProdutoRepository
{
    IQueryable<Produto> GetQueryable();
    Task Create(Produto produto);
}