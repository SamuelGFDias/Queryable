# Queryable.DynamicFilter

[![NuGet](https://img.shields.io/nuget/v/Queryable.DynamicFilter.svg)](https://www.nuget.org/packages/Queryable.DynamicFilter)
[![Publish to NuGet](https://github.com/SamuelGFDias/Queryable/actions/workflows/publish.yml/badge.svg)](https://github.com/SamuelGFDias/Queryable/actions/workflows/publish.yml)

Filtro, ordenação e paginação dinâmicos para APIs ASP.NET Core, dirigidos por query string. Em vez de escrever um `if` para cada combinação de filtro possível, o cliente da API envia `campo__operador=valor` e a biblioteca monta a `Expression<Func<T, bool>>` correspondente em cima do seu `IQueryable<T>` — funciona com Entity Framework Core, mas não depende dele.

## Os dois pacotes

| Pacote | O que faz | Quando usar |
| --- | --- | --- |
| `Queryable.DynamicFilter` | Núcleo: constrói filtro (`IFilterBuilder`), ordenação (`ISortBuilder`) e aplica os dois sobre qualquer `IQueryable<T>` (`IQuerySpecApplier`). Sem dependência de EF Core. | Você quer montar a página manualmente, ou sua fonte de dados não é EF Core (qualquer provider LINQ). |
| `Queryable.DynamicFilter.EntityFrameworkCore` | Adiciona `IPagedQueryService`: filtro + ordenação + `CountAsync` + projeção para DTO + paginação em uma única chamada assíncrona. Depende de `Microsoft.EntityFrameworkCore` e referencia o núcleo. | Sua fonte de dados é EF Core e você quer o fluxo pronto de ponta a ponta, incluindo a contagem total. |

## Instalação

```bash
dotnet add package Queryable.DynamicFilter
```

Se você usa EF Core e quer o serviço de paginação pronto, instale também:

```bash
dotnet add package Queryable.DynamicFilter.EntityFrameworkCore
```

## Setup / DI

Núcleo — registra `IFilterBuilder`, `ISortBuilder` e `IQuerySpecApplier` como `Scoped` (idempotente: chamadas repetidas não sobrescrevem registros já feitos):

```csharp
using Queryable.Extensions;

builder.Services.AddQueryableDynamicFilter();
```

EF Core — já chama `AddQueryableDynamicFilter()` internamente e adiciona `IPagedQueryService`:

```csharp
using Queryable.EntityFrameworkCore.Extensions;

builder.Services.AddQueryableDynamicFilterEfCore();
```

Se você vai usar o **Caminho A** (binding automático de `QuerySpec<T>` a partir da query string, veja abaixo), registre também o model binder:

```csharp
using Queryable.Extensions;

builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new QuerySpecModelBinderProvider());
});
```

Esse binder só é necessário para `QuerySpec<T>` recebido via `[FromQuery]`. Se você usa `RequestQuery` (Caminho B), não precisa dele — `RequestQuery` é um POCO simples e é resolvido pelo model binder padrão do ASP.NET Core.

## Sintaxe da query string

Formato de cada filtro: `campo__operador=valor`. Sem o sufixo `__operador` (ex.: `?nome=ana`), o comportamento é `eq`.

| Operador | Significado |
| --- | --- |
| `eq` | Igual (padrão quando nenhum operador é informado) |
| `neq` | Diferente |
| `gt` | Maior que |
| `lt` | Menor que |
| `gte` | Maior ou igual |
| `lte` | Menor ou igual |
| `contains` | Contém — apenas para `string` |
| `in` | Pertence a uma lista separada por vírgula |

Propriedades aninhadas usam ponto: `categoria.nome`. Ordenação usa `sort`, com `-` para descendente e vírgula para múltiplos campos. Paginação usa `page` e `pageSize`; `skipTotalCount=true` pula o `COUNT`.

```http
GET /api/produtos?ativo=true
GET /api/produtos?valor__gte=100&valor__lte=1000
GET /api/produtos?nome__contains=notebook
GET /api/produtos?categoria.nome__eq=Perifericos
GET /api/produtos?categoriaId__in=1,2,3
GET /api/produtos?sort=-valor,nome
GET /api/produtos?page=2&pageSize=20
GET /api/produtos?ativo=true&categoria.nome__contains=tech&valor__gt=50&sort=-criadoEm,nome&page=1&pageSize=10
GET /api/produtos?skipTotalCount=true
```

## Quais campos são pesquisáveis (leia isto)

> **`[Queryable]` não restringe nada.** Hoje, toda propriedade pública do tipo — e de qualquer tipo referenciado por navegação — é filtrável e ordenável por padrão. O atributo serve **apenas** para definir um alias diferente do nome da propriedade em C#.

Isso é implementado em `PathExtension.BuildPropertyPaths<T>` (pacote núcleo): o método varre `type.GetProperties()` e monta o mapa de aliases para todas as propriedades encontradas — a checagem que excluiria propriedades sem `[Queryable]` está comentada no código-fonte atual. Ou seja: se `Produto` tem uma propriedade pública `SaldoDeCaixa` sem `[Queryable]`, ela é filtrável via `?saldoDeCaixa__gt=1000` de qualquer forma. Trate isso como superfície de exposição da API: qualquer propriedade pública de `TEntity` (e de suas navegações, recursivamente) pode ser consultada e ordenada por quem chama o endpoint, com ou sem o atributo.

```csharp
using Queryable.Attributes;

public class Produto
{
    public Guid Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    [Queryable("valor")]
    public decimal Preco { get; set; }

    public bool Ativo { get; set; }

    public DateTime CriadoEm { get; set; }

    public int CategoriaId { get; set; }

    public Categoria Categoria { get; set; } = default!;
}

public class Categoria
{
    public int Id { get; set; }

    public string Nome { get; set; } = string.Empty;
}
```

Aqui, `Preco` só é alcançável pelo alias `valor` (`valor__gte=100`) — sem o atributo ainda seria alcançável como `preco__gte=100`, mas com o alias definido o nome de exposição passa a ser o do alias. Todas as demais propriedades (`Nome`, `Ativo`, `CriadoEm`, `CategoriaId`, `Categoria.Nome`, `Categoria.Id`) são pesquisáveis pelo próprio nome, sem precisar de anotação — a correspondência é case-insensitive.

## Caminho A — query string automática com `QuerySpec<T>`

Com o model binder registrado, o controller recebe `QuerySpec<T>` já populado a partir da query string:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Interfaces;

[ApiController]
[Route("api/produtos")]
public class ProdutosController(AppDbContext context, IQuerySpecApplier querySpecApplier) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProdutoDto>>> Get(
        [FromQuery] QuerySpec<Produto> spec,
        CancellationToken ct)
    {
        IQueryable<Produto> query = context.Set<Produto>();

        IQueryable<Produto> filtered = querySpecApplier.Apply(query, spec);

        int totalCount = spec.SkipTotalCount ? 0 : await filtered.CountAsync(ct);

        List<ProdutoDto> items = await querySpecApplier
            .ApplyPaged(filtered, spec)
            .Select(p => new ProdutoDto
            {
                Id = p.Id,
                Nome = p.Nome,
                Preco = p.Preco
            })
            .ToListAsync(ct);

        return Ok(items.ToPagedResult(spec.Page, spec.PageSize, totalCount));
    }
}
```

`IQuerySpecApplier.Apply` aplica filtro (`Where`) e ordenação (`OrderBy`/`ThenBy`), mas ainda não pagina. `ApplyPaged` aplica `Skip`/`Take`. A contagem (`CountAsync`) precisa acontecer **entre os dois** — sobre o resultado de `Apply`, antes de `ApplyPaged` — porque `ApplyPaged` já corta o conjunto para o tamanho de uma página; contar depois dele daria o total da página atual, não o total do conjunto filtrado.

## Caminho B — `RequestQuery` + `IPagedQueryService` (recomendado com EF Core)

Requer o pacote `Queryable.DynamicFilter.EntityFrameworkCore`. `RequestQuery` é um modelo achatado — mais fácil de expor em Swagger/OpenAPI do que um `Dictionary<string, string>` — que a biblioteca converte internamente em `QuerySpec<T>`:

```csharp
public class RequestQuery
{
    public string? QueryFilter { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public bool SkipTotalCount { get; set; }
}
```

### Formato de `QueryFilter` — regra do separador

`QueryFilter` concatena todos os filtros em uma única string, no formato `campo__operador=valor` (sem sufixo equivale a `eq`). A regra de separação entre pares:

> Os pares são separados por `;` **se a string contiver `;`**; caso contrário, são separados por `,`.

O motivo: o valor do operador `in` já usa vírgula como separador da lista (`id__in=1,2,3`). Se o separador de pares também fosse sempre vírgula, `"id__in=1,2,3,ativo=true"` seria fatiado em `["id__in=1", "2", "3", "ativo=true"]` — e `"2"`/`"3"` não têm `=`, o que lança `ArgumentException`. Por isso, sempre que algum filtro usar `in`, use `;` como separador de pares:

```text
Certo:  "id__in=1,2,3;ativo=true"
Errado: "id__in=1,2,3,ativo=true"
```

Sem `in` na string, vírgula funciona normalmente: `"nome=Notebook,ativo=true"`.

### `ApplyFilterPaginatedAsync` com projeção explícita

```csharp
using Microsoft.AspNetCore.Mvc;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;

[ApiController]
[Route("api/produtos")]
public class ProdutosController(AppDbContext context, IPagedQueryService pagedQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProdutoDto>>> Get(
        [FromQuery] RequestQuery request,
        CancellationToken ct)
    {
        PagedResult<ProdutoDto> result = await pagedQueryService.ApplyFilterPaginatedAsync(
            context.Set<Produto>(),
            request,
            p => new ProdutoDto
            {
                Id = p.Id,
                Nome = p.Nome,
                Preco = p.Preco
            },
            afterSpec: query => query.Where(p => p.Ativo),
            ct: ct);

        return Ok(result);
    }
}
```

A consulta roda com `AsNoTracking` automaticamente. `afterSpec` é uma transformação opcional aplicada **depois** do filtro/ordenação (`Apply`) e **antes** da contagem e da paginação — útil para `Include` adicionais ou regras de segurança (multi-tenant, escopo do usuário logado) que não fazem sentido expor como filtro de query string.

`SkipTotalCount` (em `RequestQuery` ou diretamente em `QuerySpec<T>`) pula o `CountAsync` e retorna `TotalCount = 0`. Vale usar em listagens de alto volume onde o `COUNT` é caro e o cliente não precisa saber o total (ex.: scroll infinito).

## `IProjectable<TEntity, TSelf>` — projeção sem repetir a expressão

Em vez de passar a expressão de projeção em cada chamada, o próprio DTO pode declará-la como membro estático:

```csharp
using System.Linq.Expressions;
using Queryable.Core;

public class ProdutoDto : IProjectable<Produto, ProdutoDto>
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }

    public static Expression<Func<Produto, ProdutoDto>> Projection =>
        produto => new ProdutoDto
        {
            Id = produto.Id,
            Nome = produto.Nome,
            Preco = produto.Preco
        };
}
```

E a chamada usa a sobrecarga de dois parâmetros de tipo, sem passar `projection`:

```csharp
PagedResult<ProdutoDto> result = await pagedQueryService.ApplyFilterPaginatedAsync<Produto, ProdutoDto>(
    context.Set<Produto>(),
    request,
    afterSpec: query => query.Where(p => p.Ativo),
    ct: ct);
```

Ganhos sobre um mapeamento resolvido em tempo de execução: `Projection` é um membro `static abstract` (C# 11+), então esquecer de implementá-lo é erro de compilação, não falha em runtime; não há reflexão (`Activator.CreateInstance`, varredura de `GetTypes()`) para descobrir o mapeamento; e, por ser uma `Expression` (não um `Func` já compilado), o provider do EF Core traduz o `Select` para SQL — apenas as colunas usadas pelo DTO trafegam do banco, sem materializar `Produto` inteiro antes de mapear.

> **Ambiguidade de sobrecarga:** existem duas sobrecargas de `ApplyFilterPaginatedAsync` com a mesma aridade — uma recebe `projection` explícita, outra usa `TDto.Projection` via `IProjectable`. Se você passar `afterSpec` posicionalmente como terceiro argumento (por exemplo, junto com `null` no lugar de uma projeção), o compilador pode não conseguir decidir entre as duas. Use sempre o argumento nomeado `afterSpec:` para desambiguar, como nos exemplos acima.

## Formato da resposta

Ambos os caminhos devolvem `PagedResult<T>`:

```csharp
public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public PageMeta Meta { get; set; } = new();
}

public class PageMeta
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; }   // Ceiling(TotalCount / PageSize)
    public bool HasPrevious { get; } // Page > 1
    public bool HasNext { get; }     // Page < TotalPages
}
```

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

## Limitações e armadilhas conhecidas

- **Recursão sem proteção contra ciclos (o ponto mais sério).** `PathExtension.BuildPropertyPaths<T>` desce recursivamente em toda propriedade cujo tipo seja classe (`IsClass`) e não seja `string`, sem nenhuma guarda contra ciclos. Se `TEntity` tiver navegação bidirecional — por exemplo `Produto.Categoria` e `Categoria.Produtos` (`List<Produto>`) — a varredura entra em loop infinito e estoura `StackOverflowException` na primeira chamada que precisar do mapa de propriedades (filtro ou ordenação). **Workaround:** não use, como `TEntity` de `QuerySpec<T>`/`ApplyFilterPaginatedAsync`, uma entidade de EF Core com navegação de ida e volta; use uma projeção/DTO de entrada sem o lado inverso, ou quebre o ciclo com `[NotMapped]`/tipo auxiliar antes de expor a entidade a este pipeline.
- **O operador `in` só converte tipos simples.** `BuildInExpression` (em `Builders/FilterBuilder.cs`) usa apenas `Convert.ChangeType` para cada item da lista — ao contrário de `ConvertValue`, usado pelos demais operadores, que trata `Guid`, enum, `DateOnly`, `TimeOnly`, `Nullable<T>` e o literal `"null"`. Na prática, `in` **não funciona com `Guid` nem com enum** (lança exceção em runtime); use `in` apenas com tipos primitivos (`int`, `long`, `decimal`, `string`, etc.).
- **`contains` só é suportado em `string`.** Usar `contains` em qualquer outra propriedade lança `NotSupportedException`.
- **`Page` e `PageSize` ignoram valores `<= 0` silenciosamente**, mantendo o valor anterior (`Page` default `1`, `PageSize` default `10`) em vez de lançar erro — vale em `QuerySpec<T>` e, por consequência, em `RequestQuery.ToQuerySpec<T>()`.
- **Sem `sort` explícito, `SortBuilder` aplica `OrderBy(x => 0)`.** Isso garante que `Skip`/`Take` sejam avaliados de forma determinística pelo provider LINQ, mas não implica ordem estável entre páginas no banco — se os dados mudam entre duas requisições paginadas sem ordenação real, o mesmo item pode aparecer em páginas diferentes ou ser pulado.
- **Chave de filtro ou campo de ordenação desconhecido lança `ArgumentException`** (`"Campo 'X' não é pesquisável."` para filtro; mensagem equivalente para ordenação). Uma query string malformada ou com campo inexistente vira uma exceção não tratada — sem um middleware/filtro de exceção global, isso retorna 500 ao cliente em vez de 400.
- **`QueryFilter` malformado também lança `ArgumentException`** — um item sem `=` (ex.: `"ativo"`) ou com chave vazia (ex.: `"=true"`) invalida a requisição inteira.

## Licença

MIT License © 2025 Samuel G. F. Dias
