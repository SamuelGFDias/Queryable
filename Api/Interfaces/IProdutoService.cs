using Api.Dtos;
using Api.Models;
using Queryable.Core;

namespace Api.Interfaces;

public interface IProdutoService
{
    Task<PagedResult<ProdutoDto>> Buscar(QuerySpec<Produto> spec);
    Task Criar(CriarProduto produto);
}