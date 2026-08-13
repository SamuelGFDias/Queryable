using Queryable.Attributes;

namespace Queryable.Tests;

public enum Status
{
    Ativo,
    Inativo,
    Pendente
}

public class Categoria
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    // Navegação de volta para Produto propositalmente bidirecional, para exercitar
    // a guarda de coleção / ciclo de PathExtension.
    public List<Produto> Produtos { get; set; } = [];
}

public class Produto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    [Queryable("valor")]
    public decimal Preco { get; set; }

    public double Peso { get; set; }

    public bool Ativo { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateOnly DataOnly { get; set; }
    public TimeOnly HoraOnly { get; set; }
    public Status Status { get; set; }
    public int? NumeroOpcional { get; set; }
    public Guid? IdOpcional { get; set; }

    public Categoria Categoria { get; set; } = new();
}

public class Endereco
{
    public string Rua { get; set; } = string.Empty;
}

public class Pedido
{
    // Dois ramos irmãos do mesmo tipo (Endereco), ambos devem ser mapeados
    // separadamente por PathExtension.
    public Endereco EnderecoEntrega { get; set; } = new();
    public Endereco EnderecoCobranca { get; set; } = new();
}

public class No
{
    public string Nome { get; set; } = string.Empty;

    // Ciclo direto: No.Proximo é do próprio tipo No.
    public No? Proximo { get; set; }
}

// Modelo dedicado a exercitar a rejeição de campo (FieldInfo, não PropertyInfo) por
// PropertyPathExtractor — os demais modelos deste arquivo só têm propriedades.
public class ComCampoPublico
{
    public string CampoPublico = string.Empty;
}

// Cenário de navegação profunda (3 níveis), no espírito do caso real observado em produção
// (Notificacao.Paciente.Cpf.Value -> alias cpf_paciente): Registro.Pessoa.Documento.Value.
public class Documento
{
    public string Value { get; set; } = string.Empty;
}

public class Pessoa
{
    public string Nome { get; set; } = string.Empty;
    public Documento Documento { get; set; } = new();
}

public class Registro
{
    public int Id { get; set; }
    public Pessoa Pessoa { get; set; } = new();
}
