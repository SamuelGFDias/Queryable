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
    [Route("")]
    public IActionResult Get([FromQuery] QuerySpec<Produto> spec)
    {
        PagedResult<Produto> resultado = produtoService.Buscar(spec);
        return Ok(resultado);
    }

    [HttpPost]
    [Route("")]
    public async Task<IActionResult> Post([FromBody] CriarProduto produto)
    {
        await produtoService.Criar((Produto)produto);
        return Ok();
    }
}