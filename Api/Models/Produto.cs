using Queryable.Attributes;

namespace Api.Models;

public class Produto
{
    [Queryable("id")] public int Id { get; set; }
    [Queryable("nome")] public string? Descricao { get; set; }
    [Queryable("preco")] public double Preco { get; set; }
    [Queryable("criado")] public DateTime Criado { get; set; }
}