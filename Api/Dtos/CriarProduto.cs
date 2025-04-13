using Api.Models;

namespace Api.Dtos;

public class CriarProduto
{
    public string? Descricao { get; set; }
    public double Preco { get; set; }
    public DateTime Criado { get; set; } = DateTime.Now;

    public static explicit operator Produto(CriarProduto crProd)
    {
        return new Produto
        {
            Descricao = crProd.Descricao,
            Preco = crProd.Preco,
            Criado = crProd.Criado
        };
    }
}