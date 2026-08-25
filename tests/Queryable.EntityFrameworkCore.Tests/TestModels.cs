using System.Linq.Expressions;
using Queryable.Core;

namespace Queryable.EntityFrameworkCore.Tests;

/// <summary>
/// Entidade de navegação usada como alvo de filtro/projeção em navegação (<c>categoria.nome</c>).
/// Desde a Etapa 9 tem a navegação de coleção de volta para <see cref="Produto"/>
/// (bidirecional, como em EF Core real): é ela que prova, contra SQLite, a rejeição clara de
/// coleção em filtro/ordenação (issue #2) e a tradução para SQL de
/// <c>For(c =&gt; c.Produtos.Count)</c> (issue #3) — ver <see cref="ColecaoTests"/>.
/// </summary>
public class Categoria
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public List<Produto> Produtos { get; set; } = [];
}

public class CategoriaDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int QtdProdutos { get; set; }
}

public class Produto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public bool Ativo { get; set; }
    public int CategoriaId { get; set; }
    public Categoria Categoria { get; set; } = null!;
}

/// <summary>
/// DTO de projeção "manual", usado com a sobrecarga de <c>ApplyFilterPaginatedAsync</c>
/// que recebe a expressão de projeção explicitamente.
/// </summary>
public class ProdutoDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public string CategoriaNome { get; set; } = string.Empty;
}

/// <summary>
/// DTO que declara a própria projeção via <see cref="IProjectable{TEntity,TSelf}"/>, usado com
/// a sobrecarga de <c>ApplyFilterPaginatedAsync</c> que dispensa o parâmetro de projeção.
/// </summary>
public class ProdutoProjectableDto : IProjectable<Produto, ProdutoProjectableDto>
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public string CategoriaNome { get; set; } = string.Empty;

    public static Expression<Func<Produto, ProdutoProjectableDto>> Projection =>
        produto => new ProdutoProjectableDto
        {
            Id = produto.Id,
            Nome = produto.Nome,
            Preco = produto.Preco,
            CategoriaNome = produto.Categoria.Nome
        };
}
