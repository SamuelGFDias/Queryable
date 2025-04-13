using Api.Dtos;
using Api.Interfaces;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Queryable.Core;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class ProdutoController(
    IProdutoService produtoService
) : ControllerBase
{
    [HttpGet]
    [Route("buscar")]
    public async Task<ActionResult<PagedResult<ProdutoDto>>> Get([FromQuery] QuerySpec<Produto> spec)
    {
        PagedResult<ProdutoDto> resultado = await produtoService.Buscar(spec);
        return Ok(resultado);
    }

    [HttpPost]
    [Route("criar")]
    public async Task<IActionResult> Post([FromBody] CriarProduto produto)
    {
        await produtoService.Criar(produto);
        return Ok();
    }
}