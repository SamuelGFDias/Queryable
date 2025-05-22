using Api.Interfaces;
using Queryable.Attributes;

namespace Api.Models;

public class Produto : IEntity
{
    [Queryable("id")] public int Id { get; set; }
    [Queryable("nome")] public string? Descricao { get; set; }
    [Queryable("preco")] public double Preco { get; set; }
    [Queryable("criado")] public DateTime Criado { get; set; }
    [Queryable("endereco")]public required Address Address { get; set; }
}

public class Address : IEntity
{
    [Queryable("rua")] public string? Rua { get; set; }
}