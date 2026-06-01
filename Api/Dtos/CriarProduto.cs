using Api.Models;

namespace Api.Dtos;

public class CriarProduto
{
    public string? Descricao { get; set; }
    public double Preco { get; set; }
    public DateTime Criado { get; set; } = DateTime.Now;
    public string? Rua { get; set; }

    public static explicit operator Produto(CriarProduto crProd)
    {
        var address = new Address { Rua = crProd.Rua };

        return new Produto
        {
            Descricao = crProd.Descricao,
            Preco = crProd.Preco,
            Criado = crProd.Criado,
            Address = address
        };
    }
}