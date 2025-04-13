using Api.Models;
using Queryable.Core;

namespace Api.Interfaces;

public interface IProdutoService
{
    PagedResult<Produto> Buscar(QuerySpec<Produto> spec);
    Task Criar(Produto produto);
}