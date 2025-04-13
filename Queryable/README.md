# Queryable.DynamicFilter

**Filtro dinâmico via query string para ASP.NET Core APIs.**  
Facilite a construção de filtros avançados (`gt`, `in`, `contains`, `null`, etc.), ordenação múltipla e paginação em
APIs com suporte a LINQ Expression.

---

## 🚀 Instalação

```bash
dotnet add package Queryable.DynamicFilter
```

---

## 🧠 O que esse pacote resolve

- ✅ Filtros com operadores como `__eq`, `__gt`, `__in`, `__contains`, etc.
- ✅ Ordenação com múltiplos campos (ex: `?sort=nome,-preco`)
- ✅ Paginação com metadados completos (`totalPages`, `hasNext`, etc.)
- ✅ Compatível com qualquer `IQueryable<T>` (EF Core, Linq2Objects, etc.)
- ✅ Encapsulado em classes com responsabilidade única

---

## 🔍 Exemplos de filtro

```http
GET /api/produto?preco__gt=10&preco__lt=100
GET /api/produto?descricao__contains=camisa
GET /api/produto?status__in=Ativo,Inativo
GET /api/produto?criado__from=2024-01-01&criado__to=2024-12-31
GET /api/produto?nome__eq=null
```

---

## ↕️ Ordenação

```http
GET /api/produto?sort=nome,-preco
```

- `nome`: ascendente
- `-preco`: descendente

---

## 📄 Padrão de resposta

```json
{
  "items": [
    /* lista de resultados */
  ],
  "meta": {
    "page": 1,
    "pageSize": 10,
    "totalCount": 42,
    "totalPages": 5,
    "hasPrevious": false,
    "hasNext": true
  }
}
```

---

## 🛠️ Como usar

```csharp
services.AddScoped<IFilterBuilder, FilterBuilder>();
services.AddScoped<ISortBuilder, SortBuilder>();
services.AddScoped<IQuerySpecApplier, QuerySpecApplier>();

services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new QuerySpecModelBinderProvider());
});
```

### Exemplo no repository:

```csharp
public class ProdutoRepository(AppDbContext context) : IProdutoRepository
{
    public IQueryable<Produto> GetQueryable()
    {
        return context.Set<Produto>();
    }
}
```
---

### Exemplo no serviço:

```csharp
public async Task<PagedResult<ProdutoDto>> Buscar(QuerySpec<Produto> spec)
{
    IQueryable<Produto> query = repository.GetQueryable();
    IQueryable<Produto> filtered = querySpecApplier.Apply(query, spec);

    int totalCount = await filtered.CountAsync();

    IQueryable<Produto> paged = querySpecApplier.ApplyPaged(filtered, spec);

    List<ProdutoDto> dto = await paged
                                .Select(x => (ProdutoDto)x)
                                .ToListAsync();

    return dto.ToPagedResult(spec.Page, spec.PageSize, totalCount);
}
```

> Você pode ou não fazer a [conversão explicita](#definindo-dto-para-produtos) entre a Entidade e a DTO.

---

## 🧩 Anotando propriedades filtráveis

```csharp
public class Produto
{
    [Queryable("nome")]
    public string? Nome { get; set; }

    [Queryable("preco")]
    public decimal Preco { get; set; }

    [Queryable("criado")]
    public DateTime Criado { get; set; }
}
```

---

### Definindo DTO para produtos

```csharp
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
```

> O `explicit operator` me permite realizar o casting de `Produto` para `ProdutoDto` no [Service](#exemplo-no-serviço).

---

## 📦 Licença

MIT License © 2025 Samuel G. F. Dias
