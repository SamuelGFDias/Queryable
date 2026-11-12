# Setup inicial do Queryable.DynamicFilter

Passo a passo para adicionar a biblioteca a uma API ASP.NET Core (.NET 10). Os exemplos usam o
domínio `Produto`/`Categoria` do README; troque pelos tipos do projeto.

## 1. Instalar

```bash
dotnet add package Queryable.DynamicFilter
# Só se a fonte de dados for EF Core e você quiser o serviço de paginação pronto:
dotnet add package Queryable.DynamicFilter.EntityFrameworkCore
```

- Não fixe `--version`: os dois pacotes saem sempre juntos, com o mesmo número, e o comando sem
  versão resolve a última.
- Se o projeto usa gestão central de pacotes (`Directory.Packages.props` com
  `ManagePackageVersionsCentrally`), a versão vai no `<PackageVersion>` do props e o
  `<PackageReference>` do `.csproj` fica sem `Version`. Mantenha as duas entradas com o mesmo número.
- O núcleo não depende de EF Core e funciona sobre qualquer `IQueryable<T>`.

## 2. Registrar no DI (`Program.cs`)

```csharp
using Queryable.Extensions;
using Queryable.EntityFrameworkCore.Extensions; // só com o pacote EF Core

// (a) Só o núcleo, limites padrão:
builder.Services.AddQueryableDynamicFilter();

// (b) EF Core, limites padrão (já chama AddQueryableDynamicFilter() por dentro):
builder.Services.AddQueryableDynamicFilterEfCore();

// (c) EF Core com limites customizados. A ORDEM IMPORTA: a sobrecarga configurada vem antes.
builder.Services.AddQueryableDynamicFilter(limits =>
{
    limits.MaxPageSize = 500;
    limits.MaxNodes = 200;
});
builder.Services.AddQueryableDynamicFilterEfCore();
```

Na ordem inversa de (c), `AddQueryableDynamicFilterEfCore()` registra os defaults primeiro (com
`TryAddSingleton`) e a configuração é ignorada sem erro.

`FilterLimits` (namespace `Queryable.Filtering`) tem cinco tetos:

| Limite | Default | Protege |
| --- | --- | --- |
| `MaxDepth` | 6 | aninhamento de grupos/`not` em filtro composto |
| `MaxNodes` | 100 | total de nós de um filtro composto |
| `MaxExpressionLength` | 4096 | tamanho da string `filter=` / `FilterExpression` |
| `MaxInItems` | 200 | itens numa lista `in` de filtro composto |
| `MaxPageSize` | 1000 | `pageSize` máximo aceito |

### Limites mais permissivos que o default em corpo JSON

Se o projeto recebe filtro composto em JSON (`[FromBody] RequestQuery` ou `[FromBody] QuerySpec<T>`)
e configurou limites **maiores** que os defaults, registre também o conversor configurado nas
opções de JSON. Sem isso o desserializador barra pelos defaults antes de os limites do DI entrarem
em jogo:

```csharp
using Queryable.Extensions;
using Queryable.Filtering;

// Controllers (MVC)
builder.Services.AddOptions<Microsoft.AspNetCore.Mvc.JsonOptions>()
    .Configure<FilterLimits>((o, limits) => o.JsonSerializerOptions.AddQueryableFilterLimits(limits));

// Minimal API
builder.Services.AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>()
    .Configure<FilterLimits>((o, limits) => o.SerializerOptions.AddQueryableFilterLimits(limits));
```

Limites mais restritivos que os defaults funcionam sem esse registro.

## 3. Escolher o caminho de integração

### Caminho A — `[FromQuery] QuerySpec<T>` (controllers)

Registre o model binder (só é preciso para `QuerySpec<T>`):

```csharp
builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new QuerySpecModelBinderProvider());
});
```

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Queryable.Core;
using Queryable.Extensions;
using Queryable.Interfaces;

[ApiController]
[Route("api/produtos")]
public class ProdutosController(AppDbContext context, IQuerySpecApplier applier) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProdutoDto>>> Get([FromQuery] QuerySpec<Produto> spec, CancellationToken ct)
    {
        IQueryable<Produto> filtered = applier.Apply(context.Set<Produto>().AsNoTracking(), spec);

        int total = spec.SkipTotalCount ? 0 : await filtered.CountAsync(ct);   // entre Apply e ApplyPaged

        List<ProdutoDto> items = await applier.ApplyPaged(filtered, spec)
            .Select(p => new ProdutoDto { Id = p.Id, Nome = p.Nome, Preco = p.Preco })
            .ToListAsync(ct);

        return Ok(items.ToPagedResult(spec.Page, spec.PageSize, total));
    }
}
```

Pontos a saber no Caminho A:

- O binder trata como **reservadas** as chaves `page`, `pageSize`, `sort`, `skipTotalCount` e
  `filter`. **Qualquer outra chave** da query string vira filtro. Um parâmetro alheio no mesmo
  endpoint (ex.: `?api-version=2`) vai ser lido como campo e falhar com "não é pesquisável".
- Erro de sintaxe em `filter=` e limite estourado já viram `400` pelo binder (com `[ApiController]`).
  Campo inexistente e valor inválido **não**: eles estouram dentro de `Apply` (ver passo 5).
- A forma `?Filters[nome]=ana`, que o Swagger UI gera, também é aceita.
- **Minimal API** não usa model binder do MVC. Monte o spec à mão:
  ```csharp
  app.MapGet("/api/produtos", async (HttpContext http, AppDbContext db, IQuerySpecApplier applier, FilterLimits limits) =>
  {
      QuerySpec<Produto> spec = QuerySpecModelBinder<Produto>.BuildSpec(http.Request.Query, limits);
      // ... mesmo fluxo Apply -> CountAsync -> ApplyPaged
  });
  ```
  `BuildSpec` **lança** `FilterExpressionSyntaxException`/`FilterLimitExceededException` em vez de
  devolver erro de modelo; trate no passo 5.

### Caminho B — `RequestQuery` + `IPagedQueryService` (recomendado com EF Core)

Não precisa de model binder: `RequestQuery` é um POCO comum.

```csharp
using Microsoft.AspNetCore.Mvc;
using Queryable.Core;
using Queryable.EntityFrameworkCore.Interfaces;

[ApiController]
[Route("api/produtos")]
public class ProdutosController(AppDbContext context, IPagedQueryService paged) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProdutoDto>>> Get([FromQuery] RequestQuery request, CancellationToken ct)
    {
        PagedResult<ProdutoDto> result = await paged.ApplyFilterPaginatedAsync(
            context.Set<Produto>(),
            request,
            p => new ProdutoDto { Id = p.Id, Nome = p.Nome, Preco = p.Preco },
            afterSpec: q => string.IsNullOrWhiteSpace(request.Sort) ? q.OrderByDescending(p => p.CriadoEm) : q,
            ct: ct);

        return Ok(result);
    }
}
```

- `AsNoTracking` é aplicado automaticamente.
- `afterSpec` roda depois do filtro/ordenação e antes da contagem/paginação: é o lugar de regras
  de escopo (multi-tenant, usuário logado), `Include` e ordenação padrão **condicional**.
- Use sempre o argumento nomeado `afterSpec:`; passar posicionalmente pode deixar a chamada
  ambígua entre as duas sobrecargas.
- Campos de `RequestQuery`: `QueryFilter` (pares `campo__op=valor`; com `in`, use `;` como
  separador e garanta pelo menos um `;`), `FilterExpression` (mini-linguagem), `Filter` (árvore
  JSON, para POST com corpo), `Sort`, `Page`, `PageSize`, `SkipTotalCount`.
- Projeção reaproveitável: o DTO pode implementar `IProjectable<TEntity, TDto>` (membro estático
  `Projection`) e a chamada usa `ApplyFilterPaginatedAsync<Produto, ProdutoDto>(query, request, afterSpec: ..., ct: ct)`.
  Isso obriga o assembly dos DTOs a referenciar o das entidades; se não puder, mantenha a projeção
  explícita numa classe estática da camada de aplicação.

## 4. Fechar a superfície exposta (não pule)

Por padrão **toda propriedade pública** da entidade, e das navegações até 5 níveis, é filtrável e
ordenável pelo cliente. `[Queryable("alias")]` só renomeia. Para cada entidade exposta, pergunte
à pessoa se há campos sensíveis (hash de senha, documento, valores internos) e configure:

```csharp
using Queryable.Configuration;

public class ProdutoQueryConfiguration : QueryableConfiguration<Produto>
{
    public ProdutoQueryConfiguration()
    {
        For(p => p.Nome);
        For(p => p.Preco).As("valor");
        For(p => p.Categoria.Nome).As("categoria");
        For(p => p.CriadoEm).SortOnly();          // ordena, mas não filtra
        For(p => p.CodigoInterno).FilterOnly();   // filtra, mas não ordena
        ForSort(p => p.NomeSocial ?? p.Nome).As("nomeExibicao"); // ordenação por expressão; As() obrigatório
        OnlyMapped();                              // nada além do declarado acima fica exposto
    }
}
```

```csharp
builder.Services.AddQueryableConfiguration<ProdutoQueryConfiguration>();
// ou todas de um assembly:
builder.Services.AddQueryableConfigurationsFromAssembly(typeof(ProdutoQueryConfiguration).Assembly);
```

- Sem `OnlyMapped()`, a configuração é só aditiva; `Ignore(p => p.Campo)` remove um campo
  específico.
- Chamadas a `For`/`Ignore`/`OnlyMapped`/`ForSort` ficam no construtor.
- Configuração fluente é preferível a `[Queryable]` quando o projeto de domínio não deve
  referenciar o pacote.
- Coleções não são filtráveis diretamente; para filtrar/ordenar por quantidade de itens:
  `For(c => c.Produtos.Count).As("qtd_produtos")`.

## 5. Transformar erros de entrada em `400`

Fora do binder do Caminho A, entrada inválida do cliente sai como exceção do `Apply` /
`ApplyFilterPaginatedAsync` / `BuildSpec` / `ToQuerySpec`, e vira `500` se ninguém tratar:

| Exceção | Quando |
| --- | --- |
| `ArgumentException` | campo inexistente ou restrito ao outro uso, coleção usada direto, enum inválido, lista `in` vazia, `QueryFilter` malformado |
| `FilterExpressionSyntaxException` (deriva de `ArgumentException`) | sintaxe inválida na mini-linguagem |
| `FilterLimitExceededException` (deriva de `ArgumentException`) | limite de `FilterLimits` estourado |
| `FormatException` / `OverflowException` | valor que não converte para o tipo (`preco=abc`, `id=nao-e-guid`) |
| `NotSupportedException` | operador incompatível com o tipo (`contains` fora de `string`) |

A forma mais segura é tratar só em volta da chamada à biblioteca, para não mascarar bugs do
próprio código como `400`:

```csharp
try
{
    return Ok(await paged.ApplyFilterPaginatedAsync(/* ... */));
}
catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or NotSupportedException)
{
    return Problem(statusCode: StatusCodes.Status400BadRequest, detail: ex.Message);
}
```

Se o projeto já tem um `IExceptionHandler`/middleware global, a alternativa é mapear essas
exceções lá, ciente de que vai pegar também as que não vieram da biblioteca.

## 6. Conferir

- `dotnet build` limpo e testes passando.
- Requisições de exemplo:
  ```http
  GET /api/produtos?ativo=true&valor__gte=100&sort=-valor,nome&page=1&pageSize=20
  GET /api/produtos?categoriaId__in=1,2,3
  GET /api/produtos?filter=not ativo=false
  GET /api/produtos?campoInexistente=1     # 400
  GET /api/produtos?pageSize=5000          # 400
  ```
- Se houve `OnlyMapped()`, confirme que um campo não declarado responde `400`.
