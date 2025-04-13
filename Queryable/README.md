# Queryable.DynamicFilter

**Filtro dinâmico via query string para ASP.NET Core APIs.**  
Facilite a construção de filtros avançados (`gt`, `in`, `contains`, `null`, etc.), ordenação múltipla e paginação em APIs com suporte a LINQ Expression.

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
  "items": [ /* lista de resultados */ ],
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

No controller:

```csharp
[HttpGet]
public IActionResult Get(QuerySpec<Produto> spec)
{
    var resultado = _querySpecApplier.Apply(_context.Produtos, spec);
    return Ok(resultado);
}
```

---

## 🧩 Anotando propriedades filtráveis

```csharp
public class Produto
{
    [Queryable("nome")]
    public string? Nome { get; set; }

    [Queryable("preco")]
    public decimal Preco { get; set; }
}
```

---

## 📦 Licença

MIT License © 2025 Samuel G. F. Dias
