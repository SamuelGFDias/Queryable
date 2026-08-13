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

Isso é implementado em `PathExtension.BuildPropertyPaths<T>` (pacote núcleo): o método varre `type.GetProperties()` e monta o mapa de aliases para todas as propriedades encontradas — a checagem que excluiria propriedades sem `[Queryable]` está comentada no código-fonte atual. Ou seja: se `Produto` tem uma propriedade pública `SaldoDeCaixa` sem `[Queryable]`, ela é filtrável via `?saldoDeCaixa__gt=1000` de qualquer forma. Trate isso como superfície de exposição da API: qualquer propriedade pública de `TEntity` (e de suas navegações, recursivamente) pode ser consultada e ordenada por quem chama o endpoint, com ou sem o atributo. Para restringir isso — por exemplo, para impedir que uma propriedade sensível como `SenhaHash` seja consultável — veja a seção [Configuração fluent](#configuração-fluent) abaixo.

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

## Configuração fluent

Anotar a entidade de domínio com `[Queryable]` obriga o projeto de Domínio a referenciar o pacote `Queryable.DynamicFilter` só para declarar um alias — inverte a direção de dependência que uma Clean/Onion Architecture normalmente exige (Domínio não deveria depender de nada). A configuração fluent resolve isso: os aliases são declarados numa classe fora do Domínio (tipicamente perto da API ou da Infra), derivada de `QueryableConfiguration<TEntity>`.

```csharp
using Queryable.Configuration;

public class ProdutoQueryConfiguration : QueryableConfiguration<Produto>
{
    public ProdutoQueryConfiguration()
    {
        // Colapsa um caminho aninhado num alias plano — "categoria" resolve para Categoria.Nome.
        For(p => p.Categoria.Nome).As("categoria");

        // Sem As(...), o caminho fica registrado sob o alias padrão (nomes das propriedades
        // em minúsculo, separados por ponto) — equivalente a For(p => p.Ativo).As("ativo").
        For(p => p.Ativo);

        // Remove do mapa o alias automático correspondente a este caminho.
        Ignore(p => p.CriadoEm);

        // Opt-in: a partir daqui, só os aliases declarados com For(...) acima continuam
        // consultáveis para Produto — tudo o mais que a reflexão mapearia automaticamente
        // (incluindo CategoriaId, Categoria.Id etc.) deixa de existir no mapa.
        OnlyMapped();
    }
}
```

As chamadas a `For(...)`, `Ignore(...)` e `OnlyMapped()` devem ocorrer no construtor da classe derivada — a configuração roda uma única vez, na inicialização da aplicação.

### Registro no DI

```csharp
using Queryable.Extensions;

builder.Services.AddQueryableDynamicFilter();
builder.Services.AddQueryableConfiguration<ProdutoQueryConfiguration>();

// Ou, para registrar todas as configurações de um assembly de uma vez:
builder.Services.AddQueryableConfigurationsFromAssembly(typeof(ProdutoQueryConfiguration).Assembly);
```

`AddQueryableConfiguration<TConfiguration>` e `AddQueryableConfigurationsFromAssembly` são encadeáveis com `AddQueryableDynamicFilter`, em qualquer ordem entre si. O que importa é a ordem **em relação ao primeiro uso** de `IPropertyPathProvider`/`IFilterBuilder`/`ISortBuilder`: o mapa de caminhos é cacheado por tipo na primeira resolução, então registrar (ou alterar) a configuração de um tipo depois de ele já ter sido consultado não tem efeito.

### Semântica de mesclagem

O mapa final de cada tipo é composto assim, nesta ordem:

| Passo | Efeito |
| --- | --- |
| 1. Mapa automático por reflexão | Todas as propriedades públicas de `TEntity` (e navegações), como hoje — inclui aliases de `[Queryable]`. |
| 2. `Ignore(...)` | Remove do mapa o(s) alias(es) cujo caminho corresponda estruturalmente ao caminho ignorado — a comparação é pela cadeia de `PropertyInfo`, não pelo texto do alias, então `Ignore(p => p.Preco)` remove o alias mesmo que ele venha de `[Queryable("valor")]`. |
| 3. Aliases configurados (`For(...).As(...)`) | Cada alias configurado **sobrescreve** o automático de mesmo nome (ex.: `For(p => p.Categoria.Nome).As("categoria")` substitui o `categoria` que antes apontava só para `Categoria`) e **coexiste** com o alias automático aninhado (`categoria.nome` continua resolvendo, em paralelo a `categoria`) — os dois apontam para o mesmo caminho. Essa coexistência permite migrar o frontend para o novo alias sem quebrar o contrato HTTP num único deploy. |
| 4. `OnlyMapped()` | Se chamado, descarta tudo que não veio do passo 3 — o mapa final passa a conter **apenas** os aliases declarados via `For(...)` para aquele tipo. Decisão por tipo: outras entidades, configuradas ou não, não são afetadas. |

Sem `OnlyMapped()`, configurar um tipo é estritamente aditivo: nada que já era consultável por reflexão deixa de ser, mesmo depois da configuração fluente entrar em vigor. `OnlyMapped()` é a forma de fechar essa superfície — por exemplo, para impedir que um campo sensível como `SenhaHash` seja filtrável, já que sem ele qualquer propriedade pública continua exposta por padrão (ver aviso na seção anterior).

O atributo `[Queryable]` continua funcionando exatamente como antes — a configuração fluente é aditiva, não o substitui. É possível misturar os dois: uma entidade com `[Queryable("valor")]` em `Preco` e uma `QueryableConfiguration<Produto>` que só colapsa `Categoria.Nome`.

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

> **Armadilha: `in` via `QueryFilter` exige `;` em algum lugar da string, mesmo com um único filtro.** A regra acima decide o separador olhando só se a string *contém* `;` — não se há mais de um par. Então um `QueryFilter` com um único filtro `in` e sem `;` nenhum cai no separador `,`, e a própria vírgula da lista do `in` é lida como separador de pares, quebrando em pedaços sem `=` e lançando `ArgumentException`. É preciso forçar o `;`, mesmo sem um segundo filtro para separar:
>
> ```text
> Certo:  "categoriaid__in=1,2;"     (ponto-e-vírgula final, mesmo sem segundo filtro)
> Certo:  "categoriaid__in=1,2;ativo=true"
> Errado: "categoriaid__in=1,2"      (vira dois pares inválidos e lança ArgumentException)
> ```

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

## Filtros compostos via JSON (OR, agrupamento e NOT)

Até aqui, tanto a query string (`campo__operador=valor`) quanto `QueryFilter` só combinam condições por `AND` — não há como expressar "nome contém X **ou** preço maior que Y" nesses dois formatos. Para isso, `QuerySpec<T>` e `RequestQuery` têm uma propriedade adicional, `Filter`, do tipo `FilterNode?` (`Queryable.Filtering`), que aceita uma árvore de filtro composto vinda de um corpo JSON: `OR`, agrupamento arbitrário e `NOT`.

`FilterNode` é uma hierarquia de records abstrata com três implementações:

| Tipo | Campos | Significado |
| --- | --- | --- |
| `FilterCondition` | `Field`, `Operator`, `Value` | Condição folha — mesma semântica de `campo__operador=valor`. |
| `FilterGroup` | `Logic` (`And`/`Or`), `Children` | Agrupamento de nós filhos, combinados pelo operador lógico indicado. |
| `FilterNot` | `Inner` | Negação lógica do nó interno. |

A desserialização é feita por `FilterNodeJsonConverter`, já anotado em `FilterNode` via `[JsonConverter(typeof(FilterNodeJsonConverter))]` — não é preciso registrar nada em `JsonSerializerOptions`, o `System.Text.Json.JsonSerializer` padrão já resolve. O polimorfismo é decidido **por presença de campo no objeto JSON, sem discriminador de tipo (`$type`)**:

- objeto com `field` ⇒ `FilterCondition`;
- objeto com `logic` + `children` ⇒ `FilterGroup`;
- objeto com `not` ⇒ `FilterNot`.

Outras regras do conversor:

- `operator` ausente (ou vazio) numa condição assume `"eq"` — mesmo padrão do dicionário.
- Nomes de propriedade JSON (`field`, `operator`, `value`, `logic`, `children`, `not`) e o valor de `logic` (`"and"`/`"or"`) são case-insensitive.
- Uma condição folha na raiz do body — `{ "field": ..., "value": ... }`, sem `logic` em volta — é aceita diretamente, sem precisar embrulhá-la num grupo.

Os operadores aceitos dentro de `operator` são os mesmos já documentados na tabela de [Sintaxe da query string](#sintaxe-da-query-string): `eq`, `neq`, `gt`, `lt`, `gte`, `lte`, `contains` (só `string`) e `in`.

Exemplo de corpo de requisição — produtos cujo nome contenha "notebook" **ou** (preço ≥ 100 **e** ativos), usando o domínio `Produto`/`Categoria` já apresentado acima:

```json
{
  "logic": "or",
  "children": [
    { "field": "nome", "operator": "contains", "value": "notebook" },
    {
      "logic": "and",
      "children": [
        { "field": "preco", "operator": "gte", "value": "100" },
        { "field": "ativo", "value": "true" }
      ]
    }
  ]
}
```

Um controller que recebe `RequestQuery` no corpo (POST, em vez de `[FromQuery]`) repassa o `Filter` normalmente — `ToQuerySpec<T>()` copia a árvore para `QuerySpec<T>.Filter` sem tratamento especial:

```csharp
using Microsoft.AspNetCore.Mvc;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;

[ApiController]
[Route("api/produtos")]
public class ProdutosController(AppDbContext context, IPagedQueryService pagedQueryService) : ControllerBase
{
    [HttpPost("buscar")]
    public async Task<ActionResult<PagedResult<ProdutoDto>>> Buscar(
        [FromBody] RequestQuery request,
        CancellationToken ct)
    {
        PagedResult<ProdutoDto> result = await pagedQueryService.ApplyFilterPaginatedAsync(
            context.Set<Produto>(),
            request,
            p => new ProdutoDto { Id = p.Id, Nome = p.Nome, Preco = p.Preco },
            ct: ct);

        return Ok(result);
    }
}
```

**Regra de combinação com `Filters`/`QueryFilter`:** se `Filter` for nulo, nada muda — só o dicionário (`Filters`) é considerado, exatamente como antes desta funcionalidade existir. Se os dois vierem preenchidos ao mesmo tempo, o predicado final é o `AND` dos dois conjuntos de condições (`AND(Filters, Filter)`) — um nunca sobrescreve o outro. Isso é resolvido por `IFilterBuilder.BuildPredicate<T>(IDictionary<string, string>, FilterNode?)`, chamado internamente por `QuerySpecApplier.Apply`.

> A combinação por `OR`/agrupamento/`NOT` via `Filter` funciona tanto vindo do corpo JSON quanto vindo da mini-linguagem textual (seção seguinte) — as duas alimentam a mesma árvore. Só a query string tradicional (`campo__operador=valor`, que popula `QuerySpec<T>.Filters`) e `QueryFilter` continuam sendo exclusivamente `AND`; quem precisa de `OR`/agrupamento/`NOT` direto na query string usa o parâmetro `filter=`, descrito a seguir.

## Filtros compostos na query string (mini-linguagem)

A porta JSON acima resolve `OR`/agrupamento/`NOT` para quem monta o filtro programaticamente ou consegue enviar corpo de requisição, mas não ajuda quem só tem `GET` disponível. Para isso existe uma mini-linguagem textual pensada para caber num único parâmetro de query string — parênteses, `and`, `or` e `not` direto na URL, sem POST com corpo JSON.

O parâmetro reconhecido é **`filter`** na query string (`QuerySpecModelBinder<T>.BuildSpec` tem um ramo dedicado para ele — nunca cai no fallback que popula `Filters`). Em `RequestQuery`, a propriedade equivalente é **`FilterExpression`** (`string?`). Nos dois casos, o texto é interpretado por `FilterExpressionParser.Parse(string)` (`Queryable.Filtering`) e produz a mesma árvore `FilterNode` que a porta JSON gera — passa pelo mesmo compilador `FilterNode → Expression`, sem caminho de compilação separado.

```text
expr       := orExpr
orExpr     := andExpr ( "or" andExpr )*
andExpr    := unary ( "and" unary )*
unary      := "not"? primary
primary    := "(" expr ")" | comparison
comparison := field ( "__" operator )? "=" value
```

Precedência: `not` liga mais forte que `and`, que liga mais forte que `or` — `a or b and c` equivale a `a or (b and c)`, e `not a and b` equivale a `(not a) and b`. Parênteses sobrepõem qualquer precedência.

`and`, `or` e `not` são case-insensitive (`AND`, `And`, `and` são equivalentes) e só contam como palavra-chave quando aparecem como **token isolado fora de aspas** — nunca como substring de um valor não citado. Sem sufixo `__operador` na chave, o operador é `eq`, com os mesmos operadores já documentados em [Sintaxe da query string](#sintaxe-da-query-string).

### Regra de aspas (onde a maioria erra)

Um valor precisa vir entre aspas duplas sempre que contiver espaço, `(`, `)`, `,`, `=`, ou for igual — ignorando maiúsculas/minúsculas — a `and`/`or`/`not`. Dentro de aspas, `\"` é aspa literal e `\\` é barra invertida literal; nenhum outro escape é suportado. Fora de aspas não existe escape: o valor termina no primeiro espaço, `)`, `,` ou fim da string, e aspas abertas e não fechadas antes do fim da expressão são erro de sintaxe.

### Operador `in`

`in` exige uma lista entre parênteses: `id__in=(1,2,3)`. Isso resolve de vez a ambiguidade que existe em `RequestQuery.QueryFilter` (seção acima), onde a vírgula é ao mesmo tempo separador de pares `campo=valor` e separador de itens de `in`, obrigando a trocar o separador de pares para `;`. Cada item da lista segue a mesma regra de aspas de um valor simples: `tag__in=("a b",c)`.

> **Limitação conhecida:** um item de `in` cujo conteúdo resolvido contém uma vírgula literal (só possível quando o item veio entre aspas, ex.: `tag__in=("a, b",c)`) ainda não é suportado — o parser rejeita com um erro claro em vez de gerar um filtro `in` silenciosamente errado (a vírgula ficaria ambígua no CSV que o operador `in` usa internamente).

### Exemplos

| Expressão | Válida? | Por quê |
| --- | --- | --- |
| `nome=joao` | sim | equivale a `nome__eq=joao` |
| `nome__contains=jo and ativo=true` | sim | duas condições combinadas por `AND` |
| `(nome__contains=ana or nome__contains=joao) and ativo=true` | sim | `OR` interno agrupado, `AND` externo |
| `not ativo=false` | sim | negação de uma condição simples |
| `id__in=(1,2,3)` | sim | lista sem ambiguidade de separador |
| `tag__in=("a b",c)` | sim | item entre aspas sem vírgula literal — item único |
| `nome="and joão"` | sim | valor que colide com palavra-chave, escapado com aspas |
| `nome=and joão` | **não** | `and` fora de aspas é lido como palavra-chave, quebra o parse |
| `nome=joão silva` | **não** | espaço fora de aspas termina o valor antes do esperado |
| `(nome=ana` | **não** | parêntese não fechado |
| `nome="joão` | **não** | aspas não fechadas |
| `id__in=1,2,3` | **não** | lista de `in` fora de parênteses — ambígua com separador de campos |
| `tag__in=("a, b",c)` | **não** | item de `in` com vírgula literal dentro das aspas — ver limitação acima |

```http
GET /api/produtos?filter=(nome__contains=ana or nome__contains=joao) and ativo=true
GET /api/produtos?filter=not ativo=false
GET /api/produtos?filter=id__in=(1,2,3)
```

Qualquer erro de sintaxe (parêntese ou aspas não fechados, palavra-chave sem aspas em posição de valor, `in` fora de parênteses, item de `in` com vírgula literal, token sobrando no fim da expressão etc.) lança `FilterExpressionSyntaxException` — especialização de `ArgumentException`, com a propriedade `Position` (1-based, posição aproximada do erro na string de entrada). Assim como um campo de filtro/ordenação desconhecido (ver [Limitações e armadilhas conhecidas](#limitações-e-armadilhas-conhecidas)), sem um middleware de exceção essa exceção não tratada vira 500 no cliente em vez de 400.

**Combinação com a árvore JSON:** em `RequestQuery`, `Filter` (árvore JSON) e `FilterExpression` (mini-linguagem) podem ser preenchidos ao mesmo tempo. Se só um dos dois vier preenchido, `QuerySpec<T>.Filter` recebe esse valor sem alteração; se os dois vierem preenchidos, o predicado final é o `AND` dos dois — `FilterGroup(FilterLogic.And, [Filter, filtro-da-expressão])`, nunca um sobrescreve o outro. `RequestQueryExtensions.ToQuerySpec<T>()` faz essa combinação.

## Limitações e armadilhas conhecidas

- **Navegação bidirecional é segura; coleções não são navegáveis; há teto de profundidade.** `PathExtension.BuildPropertyPaths<T>` tem três guardas contra o mapeamento explodir: (1) **guarda de coleção** — uma propriedade cujo tipo implementa `IEnumerable` (e não é `string`) continua entrando no mapa de aliases, mas a recursão não desce dentro dela; isso elimina o vetor clássico de recursão infinita em navegação bidirecional de EF Core (`Produto.Categoria` / `Categoria.Produtos`) e também os aliases lixo que antes vinham de `List<T>` (`.capacity`, `.count`, `.item`); (2) **guarda de ciclo por caminho** — um tipo já presente no caminho atual não é reentrado, mas isso vale só por caminho, não globalmente, de propósito: ramos irmãos do mesmo tipo (ex.: `Pedido.EnderecoEntrega` e `Pedido.EnderecoCobranca`, ambos `Endereco`) continuam os dois mapeados; (3) **limite de profundidade** — `MaxDepth = 5` níveis de aninhamento, além disso o mapeamento simplesmente para de descer. Na prática, isso significa que `Produto.Categoria` e `Categoria.Produtos` coexistindo não quebra mais nada, mas também que não dá para filtrar *através* de uma coleção (`categoria.produtos.nome` não é endereçável — só o que estiver até 5 níveis de navegação simples de profundidade).
- **O operador `in` usa a mesma conversão de valor dos demais operadores.** `BuildInExpression` (em `Builders/FilterBuilder.cs`) compartilha o conversor escalar usado por `ConvertValue`, então `Guid`, enum, `DateOnly`, `TimeOnly`, `Nullable<T>` e o literal `"null"` funcionam normalmente dentro de `in` — por exemplo `id__in=3fa85f64-5717-4562-b3fc-2c963f66afa6,7c9e6679-7425-40de-944b-e07fc1f90ae7` funciona. Duas coisas a saber: uma lista `in` sem nenhum item válido após o split lança `ArgumentException`; e itens vazios ou só com espaço em branco na lista são ignorados silenciosamente (`id__in=1,,2` equivale a `id__in=1,2`).
- **`contains` só é suportado em `string`.** Usar `contains` em qualquer outra propriedade lança `NotSupportedException`.
- **`Page` e `PageSize` ignoram valores `<= 0` silenciosamente**, mantendo o valor anterior (`Page` default `1`, `PageSize` default `10`) em vez de lançar erro — vale em `QuerySpec<T>` e, por consequência, em `RequestQuery.ToQuerySpec<T>()`.
- **Sem `sort` explícito, `SortBuilder` aplica `OrderBy(x => 0)`.** Isso garante que `Skip`/`Take` sejam avaliados de forma determinística pelo provider LINQ, mas não implica ordem estável entre páginas no banco — se os dados mudam entre duas requisições paginadas sem ordenação real, o mesmo item pode aparecer em páginas diferentes ou ser pulado.
- **Chave de filtro ou campo de ordenação desconhecido lança `ArgumentException`** (`"Campo 'X' não é pesquisável."` para filtro; mensagem equivalente para ordenação). Uma query string malformada ou com campo inexistente vira uma exceção não tratada — sem um middleware/filtro de exceção global, isso retorna 500 ao cliente em vez de 400.
- **`QueryFilter` malformado também lança `ArgumentException`** — um item sem `=` (ex.: `"ativo"`) ou com chave vazia (ex.: `"=true"`) invalida a requisição inteira.

## Testes

O repositório tem suíte automatizada em `tests/Queryable.Tests` (núcleo) e `tests/Queryable.EntityFrameworkCore.Tests` (integração EF Core, contra SQLite in-memory — necessário para pegar erro de tradução para SQL que uma lista em memória não revelaria). Roda com:

```bash
dotnet test
```

## Licença

MIT License © 2025 Samuel G. F. Dias
