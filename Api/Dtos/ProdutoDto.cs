using Api.Models;

namespace Api.Dtos;

public class ProdutoDto
{
    public string? Descricao { get; init; }
    public double Preco { get; init; }
    public DateTime Criacao { get; init; }

    public static explicit operator ProdutoDto(Produto produto)
    {
        return new ProdutoDto
        {
            Descricao = produto.Descricao,
            Preco = produto.Preco,
            Criacao = produto.Criado
        };
    }
}