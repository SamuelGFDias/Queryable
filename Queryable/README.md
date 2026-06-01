# Queryable.DynamicFilter

Biblioteca para aplicar filtros dinâmicos, ordenação e paginação em `IQueryable<T>` no ASP.NET Core, com suporte a propriedades aninhadas e aliases via atributo.

## Instalação

```bash
dotnet add package Queryable.DynamicFilter --version 10.0.0
```

## Como usar

### 1) Anote as propriedades pesquisáveis

Use `[Queryable]` para expor campos que podem ser filtrados/ordenados.

```csharp
using Queryable.Attributes;

public class Produto
{
    [Queryable("id")]
    public Guid Id { get; set; }

    [Queryable("nome")]
    public string Nome { get; set; } = string.Empty;

    [Queryable("preco")]
    public decimal Preco { get; set; }

    [Queryable("ativo")]
    public bool Ativo { get; set; }

    [Queryable("criadoEm")]
    public DateTime CriadoEm { get; set; }

    [Queryable("categoria")]
    public Categoria Categoria { get; set; } = default!;
}

public class Categoria
{
    [Queryable("nome")]
    public string Nome { get; set; } = string.Empty;
}
```

### 2) Registre os serviços e o model binder

```csharp
using Queryable.Builders;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Interfaces;

services.AddScoped<IFilterBuilder, FilterBuilder>();
services.AddScoped<ISortBuilder, SortBuilder>();
services.AddScoped<IQuerySpecApplier, QuerySpecApplier>();

services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new QuerySpecModelBinderProvider());
});
```

### 3) Use `QuerySpec<T>` no endpoint

```csharp
[HttpGet]
public async Task<ActionResult<PagedResult<ProdutoDto>>> Get([FromQuery] QuerySpec<Produto> spec)
{
    IQueryable<Produto> query = _context.Set<Produto>();

    IQueryable<Produto> filtered = _querySpecApplier.Apply(query, spec);

    int totalCount = spec.SkipTotalCount ? 0 : await filtered.CountAsync();

    List<ProdutoDto> items = await _querySpecApplier
        .ApplyPaged(filtered, spec)
        .Select(p => new ProdutoDto
        {
            Id = p.Id,
            Nome = p.Nome,
            Preco = p.Preco
        })
        .ToListAsync();

    return Ok(items.ToPagedResult(spec.Page, spec.PageSize, totalCount));
}
```

## Sintaxe da query string

### Operadores suportados

- `eq` (padrão): igualdade
- `neq`: diferente
- `gt`: maior que
- `lt`: menor que
- `gte`: maior ou igual
- `lte`: menor ou igual
- `contains`: contém (somente `string`)
- `in`: pertence a lista CSV

Formato: `campo__operador=valor`

Sem operador (`?nome=ana`) o comportamento é `eq`.

### Exemplos

```http
GET /api/produtos?ativo=true&preco__gte=100&preco__lte=1000&nome__contains=note
GET /api/produtos?categoria.nome__eq=Perifericos
GET /api/produtos?id__in=3fa85f64-5717-4562-b3fc-2c963f66afa6,8f14e45f-e8b2-4f7d-9c1b-1b6a1f2d3c4e
GET /api/produtos?sort=-preco,nome
GET /api/produtos?page=2&pageSize=20
GET /api/produtos?ativo=true&categoria.nome__contains=tech&preco__gt=50&sort=-criadoEm,nome&page=1&pageSize=10
```

## Tipos de dados públicos

### `QueryableAttribute`

Arquivo: `Queryable/Attributes/QueryableAttribute.cs`

- `string? Alias { get; set; }`

### `QuerySpec<T>`

Arquivo: `Queryable/Core/QuerySpec.cs`

- `Dictionary<string, string> Filters { get; set; } = new();`
- `string? Sort { get; set; }`
- `int Page { get; set; }` (default: `1`, ignora valores `<= 0`)
- `int PageSize { get; set; }` (default: `10`, ignora valores `<= 0`)
- `bool SkipTotalCount { get; set; }`

### `PagedResult<T>`

Arquivo: `Queryable/Core/PagedResult.cs`

- `List<T> Items { get; set; } = [];`
- `PageMeta Meta { get; set; } = new();`

### `PageMeta`

Arquivo: `Queryable/Core/PageMeta.cs`

- `int Page { get; init; }`
- `int PageSize { get; init; }`
- `int TotalCount { get; init; }`
- `int TotalPages { get; }`
- `bool HasPrevious { get; }`
- `bool HasNext { get; }`

## Services e métodos

### Interfaces

`Queryable/Interfaces/IFilterBuilder.cs`

- `Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams)`

`Queryable/Interfaces/ISortBuilder.cs`

- `IOrderedQueryable<T> ApplySort<T>(IQueryable<T> query, string? sortFields)`

`Queryable/Interfaces/IQuerySpecApplier.cs`

- `IQueryable<T> Apply<T>(IQueryable<T> query, QuerySpec<T> spec)`
- `IQueryable<T> ApplyPaged<T>(IQueryable<T> query, QuerySpec<T> spec)`

### Implementações

`Queryable/Builders/FilterBuilder.cs`

- `BuildPredicate<T>(...)`: monta `Expression<Func<T, bool>>` com os operadores suportados.

`Queryable/Builders/SortBuilder.cs`

- `ApplySort<T>(...)`: aplica ordenação simples/múltipla por aliases e campos aninhados.

`Queryable/Core/QuerySpecApplier.cs`

- `Apply<T>(...)`: aplica filtro e ordenação.
- `ApplyPaged<T>(...)`: aplica `Skip/Take` para paginação.

### Extensões auxiliares

`Queryable/Extensions/PaginationExtensions.cs`

- `PagedResult<T> ToPagedResult<T>(this List<T> items, int page, int pageSize, int totalCount)`

`Queryable/Extensions/QuerySpecModelBinder.cs`

- `Task BindModelAsync(ModelBindingContext bindingContext)`

`Queryable/Extensions/QuerySpecModelBinderProvider.cs`

- `IModelBinder? GetBinder(ModelBinderProviderContext context)`

## Resposta paginada esperada

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "nome": "Notebook",
      "preco": 4599.90
    }
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

## Licença

MIT License © 2025 Samuel G. F. Dias
