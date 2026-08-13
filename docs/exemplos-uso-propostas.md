# Exemplos de uso — se as propostas fossem implementadas

> **AVISO — leia antes de copiar qualquer trecho deste documento.**
>
> - Este documento é **ILUSTRATIVO** de duas propostas ainda **NÃO IMPLEMENTADAS**:
>   [`docs/proposta-configuracao-fluent.md`](./proposta-configuracao-fluent.md) e
>   [`docs/proposta-filtros-compostos.md`](./proposta-filtros-compostos.md). Nenhuma linha de
>   código do repositório foi alterada para produzir os exemplos abaixo.
> - Todo bloco marcado **"Depois (proposto, não compila hoje)"** é hipotético. Ele **não compila**
>   contra a versão atual da lib — `QueryableConfiguration<T>`, `IPropertyPathProvider`,
>   `FilterNode`, `QuerySpec<T>.Filter`, `AddQueryableConfigurationsFromAssembly` e o parâmetro de
>   mini-linguagem em `RequestQuery` **não existem no código hoje**. Não copie para um projeto real
>   esperando que funcione.
> - Todo bloco marcado **"Hoje (funciona)"** reflete a biblioteca exatamente como ela é na versão
>   atual do repositório e **funciona**, conferido linha a linha contra `src/`.
>
> Objetivo: dar ao time material concreto — código de uso, não só descrição de API — para avaliar
> as duas propostas.

---

## Domínio do exemplo

Usado do início ao fim deste documento. Camada de Domínio, sem nenhuma referência à biblioteca:

```csharp
// Sindras.Domain — nenhuma referência a Queryable.DynamicFilter
public class Usuario
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public Cpf Cpf { get; private set; } = default!;
    public Email Email { get; private set; } = default!;
    public bool Ativo { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public string SenhaHash { get; private set; } = string.Empty;  // NUNCA deve ser consultável
    public Perfil Perfil { get; private set; } = default!;
}

public class Cpf     { public string Value { get; private set; } = string.Empty; }
public class Email   { public string Value { get; private set; } = string.Empty; }
public class Perfil  { public int Id { get; private set; } public string Descricao { get; private set; } = string.Empty; }
```

---

## 1. O que muda, em uma tabela

| Cenário | Hoje | Depois (se as duas propostas forem implementadas) |
|---|---|---|
| Filtrar por CPF | `?cpf.value=12345678900` (vaza o nome interno do value object) | `?cpf=12345678900` (alias plano, via `For(u => u.Cpf.Value).As("cpf")`) |
| Expor `SenhaHash` | Consultável — é propriedade pública, e a checagem que exigiria `[Queryable]` está comentada em `PathExtension.cs` | Bloqueado — com `OnlyMapped()` em `UsuarioQueryConfiguration`, só o que foi mapeado com `For(...)` existe |
| Anotação no domínio para expor/renomear campo | Necessária: `[Queryable("alias")]` na propriedade, o que obriga `Sindras.Domain` a referenciar o pacote NuGet `Queryable.DynamicFilter` | Nenhuma — o mapeamento vive em `UsuarioQueryConfiguration`, na Infraestrutura |
| OR / agrupamento de condições | Impossível — `FilterBuilder` só combina com `Expression.AndAlso` (`FilterBuilder.cs`, linha 49-51) | Disponível via árvore `FilterNode` (`FilterGroup(FilterLogic.Or, ...)`), pela mini-linguagem ou pela porta JSON |
| Lista `in` | Exige troca do separador de pares para `;` na string de `RequestQuery.QueryFilter` (ex.: `id__in=1,2,3;ativo=true`), porque a vírgula já separa pares | Lista entre parênteses na mini-linguagem: `id__in=(1,2,3)`, sem ambiguidade de separador |

---

## 2. Backend — hoje

### 2.1 Anotação obrigatória para renomear um campo

Hoje, para que `?cpf=...` funcione em vez de `?cpf.value=...`, seria preciso anotar a propriedade
folha do value object com `[Queryable]`. Mas, como descrito na seção 3 da proposta de configuração
fluent, isso **não resolve** o caso de `Usuario.Cpf.Value`: o alias de uma propriedade aninhada é
sempre `prefixo + "." + alias-local` (`PathExtension.cs`, `BuildPathsRecursively`), então anotar
`Cpf.Value` com `[Queryable("cpf")]` produziria `cpf.cpf`, não `cpf`. E anotar o tipo `Cpf`
inteiro afetaria toda entidade que o usa, porque é um value object compartilhado.

**Hoje (funciona)**

```csharp
// Domínio precisa referenciar o pacote Queryable.DynamicFilter só para isto.
using Queryable.Attributes;

public class Usuario
{
    [Queryable("nome")]
    public string Nome { get; private set; } = string.Empty;

    // Não existe forma de fazer "?cpf=..." resolver para Cpf.Value hoje.
    // O único caminho endereçável para o CPF é "cpf.value" (nome da propriedade
    // de navegação "Cpf" + nome da propriedade folha "Value"), goste-se ou não.
    public Cpf Cpf { get; private set; } = default!;

    // SenhaHash é propriedade pública comum: sem [Queryable] nenhum, mesmo assim
    // é filtrável e ordenável hoje, porque a checagem "if (attr == null) continue;"
    // está comentada em PathExtension.cs.
    public string SenhaHash { get; private set; } = string.Empty;
}
```

### 2.2 Registro no DI

**Hoje (funciona)**

```csharp
// Program.cs
builder.Services.AddQueryableDynamicFilterEfCore();
```

Isso registra (via `Queryable.EntityFrameworkCore.Extensions.ServiceCollectionExtensions`) o
núcleo — `IFilterBuilder`, `ISortBuilder`, `IQuerySpecApplier` — mais `IPagedQueryService`, todos
como `Scoped`.

### 2.3 Controller com `RequestQuery` + `IPagedQueryService`

**Hoje (funciona)**

```csharp
[ApiController]
[Route("api/usuarios")]
public class UsuariosController(
    AppDbContext db,
    IPagedQueryService pagedQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<UsuarioDto>>> Listar(
        [FromQuery] RequestQuery request,
        CancellationToken ct)
    {
        PagedResult<UsuarioDto> resultado = await pagedQueryService
            .ApplyFilterPaginatedAsync(
                db.Usuarios.AsQueryable(),
                request,
                UsuarioProjections.ToDto,
                ct: ct);

        return Ok(resultado);
    }
}
```

A sobrecarga usada aqui é a de projeção explícita — `Expression<Func<TEntity, TDto>> projection`
como terceiro argumento, obrigatório — cujo caminho recomendado para uma arquitetura em camadas
(`UsuarioProjections.ToDto`, em Application) está detalhado na seção 4.1. Existe também uma
sobrecarga alternativa que dispensa esse argumento resolvendo `TDto.Projection` via
`IProjectable<TEntity, TDto>` — ver seção 4.2 — mas ela exige que o próprio `UsuarioDto` implemente
a interface, o que só é viável quando o assembly do DTO pode referenciar o assembly da entidade.

### 2.4 Query string real de hoje — incluindo a pegadinha do `;`

**Hoje (funciona)**

```http
GET /api/usuarios?QueryFilter=cpf.value=12345678900&Sort=nome&Page=1&PageSize=10
```

Filtrar por nome contendo texto **e** por uma lista de IDs de perfil ao mesmo tempo exige trocar o
separador de pares para `;`, porque a vírgula já é o separador de itens do `in`:

```http
GET /api/usuarios?QueryFilter=nome__contains=jo;perfil.id__in=1,2,3&Sort=-criadoEm
```

Se alguém usar `,` como separador de pares junto com `in` (a pegadinha), o parser
(`RequestQueryExtensions.ParseQueryFilter`) trata a vírgula da lista como mais um separador de par
e o resultado sai errado silenciosamente — por isso o comentário no próprio código orienta a usar
`;` sempre que houver um `in` no meio:

```http
REM Errado (comportamento inesperado, e não lança erro):
GET /api/usuarios?QueryFilter=perfil.id__in=1,2,3,ativo=true
```

---

## 3. Backend — depois: configuração

### 3.1 `UsuarioQueryConfiguration`

**Depois (proposto, não compila hoje)**

```csharp
// Sindras.Infrastructure.Queryable/UsuarioQueryConfiguration.cs
using Queryable.Configuration; // tipo proposto, não existe hoje
using Sindras.Domain;

public sealed class UsuarioQueryConfiguration : QueryableConfiguration<Usuario>
{
    public UsuarioQueryConfiguration()
    {
        OnlyMapped(); // modo opt-in: só o que for declarado abaixo fica endereçável

        For(u => u.Nome).As("nome");
        For(u => u.Cpf.Value).As("cpf");            // colapsa o value object
        For(u => u.Email.Value).As("email");
        For(u => u.Ativo).As("ativo");
        For(u => u.CriadoEm).As("criadoEm");
        For(u => u.Perfil.Descricao).As("perfil");

        // SenhaHash não aparece em nenhum For(...): com OnlyMapped() ativo,
        // fica automaticamente inacessível a filtro e ordenação — nenhum Ignore()
        // extra é necessário.
    }
}
```

### 3.2 Registro no DI

**Depois (proposto, não compila hoje)**

```csharp
// Program.cs
builder.Services.AddQueryableDynamicFilterEfCore();
builder.Services.AddQueryableConfigurationsFromAssembly(
    typeof(UsuarioQueryConfiguration).Assembly);
```

`AddQueryableDynamicFilterEfCore()` continua sendo chamado exatamente como hoje (seção 2.2) — a
configuração fluent é uma camada adicional por cima, não uma substituição.

### 3.3 Diagrama de camadas

```text
Sindras.Domain
  └─ Usuario, Cpf, Email, Perfil            (nenhuma referência a Queryable.DynamicFilter)

Sindras.Infrastructure
  └─ Queryable/
       UsuarioQueryConfiguration.cs         (referencia Queryable.DynamicFilter)
  └─ AppDbContext, mapeamento EF Core

Sindras.Api
  └─ Program.cs                             (AddQueryableDynamicFilterEfCore + AddQueryableConfigurationsFromAssembly)
  └─ Controllers/UsuariosController.cs
```

O ponto central: com a configuração fluent, só a Infraestrutura referencia
`Queryable.DynamicFilter`. O Domínio (`Usuario`, `Cpf`, `Email`, `Perfil`) fica limpo — nenhum
`[Queryable]`, nenhum `using Queryable.Attributes`.

---

## 4. Backend — depois: DTO e projeção

Existem dois caminhos hoje para ligar `Usuario` a `UsuarioDto`. Para uma arquitetura em camadas
como a de Sindras — onde `Sindras.Application.Contracts` (o projeto dos DTOs) **não referencia**
`Sindras.Domain`, por fronteira deliberada — só um dos dois se aplica sem violar essa fronteira.
Os dois são apresentados abaixo, na ordem de recomendação.

### 4.1 Caminho recomendado — projeção declarada na camada Application

A camada `Sindras.Application` já referencia tanto `Sindras.Domain` quanto
`Sindras.Application.Contracts` (é ela quem orquestra os dois lados), então é o lugar natural para
casar entidade e DTO. Em vez de o DTO implementar uma interface genérica sobre a entidade, a
projeção vira uma classe estática dedicada, em Application:

**Hoje (funciona)**

```csharp
// Sindras.Application/Projections/UsuarioProjections.cs
// Application já referencia Domain E Contracts — Contracts fica intocado.
using System.Linq.Expressions;
using Sindras.Application.Contracts;
using Sindras.Domain;

public static class UsuarioProjections
{
    public static Expression<Func<Usuario, UsuarioDto>> ToDto =>
        u => new UsuarioDto
        {
            Id     = u.Id,
            Nome   = u.Nome,
            Cpf    = u.Cpf.Value,
            Perfil = u.Perfil.Descricao
        };
}
```

E o uso no serviço/controller passa a ser a sobrecarga de `IPagedQueryService` com projeção
explícita:

```csharp
await _pagedQuery.ApplyFilterPaginatedAsync(
    _db.Usuarios, request, UsuarioProjections.ToDto, ct: ct);
```

Essa sobrecarga — `ApplyFilterPaginatedAsync<TEntity, TDto>(query, request,
Expression<Func<TEntity, TDto>> projection, afterSpec, ct)` — **já existe na versão atual**, ver
`src/Queryable.EntityFrameworkCore/Interfaces/IPagedQueryService.cs`. Não depende de nenhuma das
duas propostas deste documento.

Pontos importantes:

- Continua sendo uma `Expression`, então o `Select` é traduzido para SQL pelo EF Core e só as
  colunas do DTO trafegam do banco — sem materializar a entidade inteira, sem reflexão em tempo de
  execução.
- Não se perde a garantia de "não esquecer a projeção": nesta sobrecarga o parâmetro `projection` é
  **obrigatório** (não tem valor padrão), então o compilador cobra no ponto de chamada assim como
  cobraria a implementação de uma interface. O que muda é só **onde** essa obrigação é cobrada — na
  chamada ao serviço, em Application, em vez de na declaração do DTO, em Contracts.
- `UsuarioDto`, em `Sindras.Application.Contracts`, permanece um DTO comum — sem implementar
  interface nenhuma e sem qualquer referência a `Sindras.Domain` ou a `Queryable.DynamicFilter`.

### 4.2 Caminho alternativo — `IProjectable<TEntity, TSelf>` no próprio DTO

`IProjectable<TEntity, TSelf>` **já existe hoje** (`src/Queryable/Core/IProjectable.cs`) — não é
parte de nenhuma das duas propostas deste documento. É uma alternativa válida ao caminho da seção
4.1, mas com um pré-requisito que a torna **inaplicável a Sindras**: só serve quando o assembly dos
DTOs pode referenciar o assembly das entidades. Se os DTOs vivem num projeto de contratos
deliberadamente isolado do domínio — como `Sindras.Application.Contracts` — este caminho não se
aplica, porque fechar `IProjectable<Usuario, UsuarioDto>` no DTO obrigaria Contracts a referenciar
Domain, e essa referência vazaria transitivamente para quem consome Contracts (`Sindras.IoC`,
`Sindras.Infra.BackgroundJobs`).

**Alternativa (funciona, mas exige DTO e entidade no mesmo lado da fronteira de referência)**

```csharp
using Queryable.Core;
using Sindras.Domain;

public class UsuarioDto : IProjectable<Usuario, UsuarioDto>
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool Ativo { get; set; }
    public DateTime CriadoEm { get; set; }
    public string Perfil { get; set; } = string.Empty;

    public static Expression<Func<Usuario, UsuarioDto>> Projection =>
        u => new UsuarioDto
        {
            Id = u.Id,
            Nome = u.Nome,
            Cpf = u.Cpf.Value,
            Email = u.Email.Value,
            Ativo = u.Ativo,
            CriadoEm = u.CriadoEm,
            Perfil = u.Perfil.Descricao
        };
}
```

`IProjectable` fica declarado no pacote núcleo (`Queryable.Core`, em `src/Queryable`), não no
pacote de EF Core — então o assembly dos DTOs não precisa referenciar Entity Framework só para
implementar a interface. Isso resolve o acoplamento com **EF Core**, mas é um eixo diferente do
acoplamento com o **domínio**: `IProjectable<Usuario, UsuarioDto>` ainda fecha o genérico sobre
`Usuario`, então o assembly do DTO referencia o assembly da entidade de qualquer forma, EF Core à
parte.

Em ambos os caminhos, `SenhaHash` simplesmente não aparece no DTO — isso já é verdade hoje,
independente das propostas. O que as propostas endereçam é o **filtro** (`?senhaHash=...` ainda
funcionaria hoje mesmo com esse DTO, porque filtro e projeção são mecanismos independentes); a
projeção nunca expôs `SenhaHash`.

---

## 5. Backend — depois: endpoints

### 5.1 `GET` com mini-linguagem (depende da proposta de filtros compostos, fase 3)

**Depois (proposto, não compila hoje)**

```csharp
[HttpGet]
public async Task<ActionResult<PagedResult<UsuarioDto>>> Listar(
    [FromQuery] RequestQuery request,   // ganha propriedade nova para a mini-linguagem
    CancellationToken ct)
{
    // Já disponível hoje: a sobrecarga de ApplyFilterPaginatedAsync com projeção
    // explícita (UsuarioProjections.ToDto, seção 4.1). O que muda é o que RequestQuery
    // consegue carregar: com a mini-linguagem, o parâmetro de filtro (nome ainda em
    // aberto na proposta — seção 12 daquele documento; sugestão de trabalho: "filter")
    // aceita OR, agrupamento e NOT.
    PagedResult<UsuarioDto> resultado = await pagedQueryService
        .ApplyFilterPaginatedAsync(
            db.Usuarios.AsQueryable(),
            request,
            UsuarioProjections.ToDto,
            ct: ct);

    return Ok(resultado);
}
```

O que já funciona hoje neste trecho: a assinatura do controller, `RequestQuery` como parâmetro,
`IPagedQueryService.ApplyFilterPaginatedAsync<TEntity, TDto>` na sobrecarga de projeção explícita
(seção 4.1). O caminho alternativo com `IProjectable` (seção 4.2) também funcionaria aqui, trocando
o terceiro argumento pela ausência dele — mas exigiria `UsuarioDto` referenciando `Usuario`. O que é
proposto: a nova propriedade de `RequestQuery` para a mini-linguagem (fase 3 de
`proposta-filtros-compostos.md`) e sua resolução de aliases por `UsuarioQueryConfiguration` (fase 2
de `proposta-configuracao-fluent.md`, via `IPropertyPathProvider`).

### 5.2 `POST /busca` com árvore JSON (depende da proposta de filtros compostos, fase 2)

**Depois (proposto, não compila hoje)**

```csharp
[HttpPost("busca")]
public async Task<ActionResult<PagedResult<UsuarioDto>>> Buscar(
    [FromBody] FilterNode filtro,
    [FromQuery] string? sort,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] bool skipTotalCount = false,
    CancellationToken ct = default)
{
    var spec = new QuerySpec<Usuario>
    {
        Filter = filtro,       // QuerySpec<T>.Filter — proposto, tipo FilterNode?
        Sort = sort,
        Page = page,
        PageSize = pageSize,
        SkipTotalCount = skipTotalCount
    };

    // Ilustrativo: nesta proposta IQuerySpecApplier/IPagedQueryService ganhariam
    // um caminho que aceita QuerySpec<T> já montado (com Filter preenchido),
    // não só RequestQuery. O mecanismo de compilação FilterNode -> Expression
    // é o mesmo compilador descrito na proposta de filtros compostos, seção 3.2.
    PagedResult<UsuarioDto> resultado = await pagedQueryService
        .ApplyFilterSpecPaginatedAsync<Usuario, UsuarioDto>(db.Usuarios.AsQueryable(), spec, ct: ct);

    return Ok(resultado);
}
```

`FilterNode` no corpo, `QuerySpec<T>.Filter` e o método de serviço que aceita um `QuerySpec<T>`
pronto (em vez de `RequestQuery`) são inteiramente propostos — nada disso existe em
`IPagedQueryService` hoje (`ApplyFilterPaginatedAsync` só aceita `RequestQuery`, ver
`src/Queryable.EntityFrameworkCore/Interfaces/IPagedQueryService.cs`). Este endpoint existe para
ilustrar o caso de uso "frontend monta a árvore e manda no corpo", central à proposta de filtros
compostos.

---

## 6. Frontend — query string (GET)

Todos os exemplos abaixo assumem as duas propostas implementadas (`UsuarioQueryConfiguration` com
os aliases da seção 3, mini-linguagem da fase 3 de `proposta-filtros-compostos.md` acoplada a
`RequestQuery`).

| URL | Resultado esperado |
|---|---|
| `GET /api/usuarios?filter=cpf="12345678900"` | Usuários com CPF exatamente igual a `12345678900` (resolve via alias `cpf`, sem `[Queryable]` no domínio) |
| `GET /api/usuarios?filter=criadoEm__gte="2026-01-01"` | Usuários criados em ou após 1º de janeiro de 2026 |
| `GET /api/usuarios?filter=perfil__in=("Administrador","Gestor")` | Usuários cujo perfil é `Administrador` ou `Gestor`, via lista `in` entre parênteses |
| `GET /api/usuarios?filter=(perfil="Administrador" or perfil="Gestor") and ativo=true` | Usuários ativos cujo perfil é `Administrador` ou `Gestor` — agrupamento com `OR` dentro de um `AND`, impossível hoje |
| `GET /api/usuarios?filter=not ativo=false` | Usuários para os quais "ativo = false" é negado — equivalente a `ativo=true`, mas usando o operador `not` |
| `GET /api/usuarios?filter=nome__contains="jo" and ativo=true&sort=-criadoEm&page=2&pageSize=20&skipTotalCount=true` | Usuários ativos com "jo" no nome, ordenados por data de criação decrescente, página 2 de 20 itens, sem contagem total (`meta.totalCount` sai `0`, `totalPages` sai `0`) |

### Exemplos inválidos

```http
GET /api/usuarios?filter=nome=joão silva
```

Erro esperado: falha de parse — espaço fora de aspas termina o valor antes do esperado
(`silva` seria interpretado como token solto, não como continuação do valor). Conforme a tabela da
seção 4 de `proposta-filtros-compostos.md`, valores com espaço **precisam** estar entre aspas:
`filter=nome="joão silva"`.

```http
GET /api/usuarios?filter=senhaHash="qualquercoisa"
```

Erro esperado: `senhaHash` não é um campo mapeado em `UsuarioQueryConfiguration` (que chama
`OnlyMapped()`), então não existe no mapa de aliases resolvido por `IPropertyPathProvider` para
`Usuario` — o mesmo tipo de erro que `FilterBuilder.BuildPredicate` já lança hoje para um campo
desconhecido (`ArgumentException("Campo 'x' não é pesquisável.")`, `FilterBuilder.cs`, linha 26),
só que agora bloqueando `senhaHash` deliberadamente por causa do `OnlyMapped()`, não porque o campo
não existe na entidade.

---

## 7. Frontend — árvore JSON (POST)

### 7.1 Corpo JSON equivalente ao exemplo de OR da seção 6

**Depois (proposto, não compila hoje)**

```json
{
  "logic": "and",
  "children": [
    {
      "logic": "or",
      "children": [
        { "field": "perfil", "operator": "eq", "value": "Administrador" },
        { "field": "perfil", "operator": "eq", "value": "Gestor" }
      ]
    },
    { "field": "ativo", "value": "true" }
  ]
}
```

Enviado para `POST /api/usuarios/busca` (seção 5.2).

### 7.2 Tipos TypeScript

```ts
type FilterOperator =
  | 'eq'
  | 'neq'
  | 'gt'
  | 'lt'
  | 'gte'
  | 'lte'
  | 'contains'
  | 'in';

type FilterNode =
  | { field: string; operator?: FilterOperator; value: string }
  | { logic: 'and' | 'or'; children: FilterNode[] }
  | { not: FilterNode };
```

`FilterOperator` lista os operadores reais suportados hoje por `FilterBuilder`
(`src/Queryable/Builders/FilterBuilder.cs`, `SupportedOperators`, linha 11): `eq`, `neq`, `gt`,
`lt`, `gte`, `lte`, `contains`, `in`. A proposta de filtros compostos reaproveita exatamente essa
lista — nenhum operador novo é introduzido, só a forma de combinar condições (`and`/`or`/`not`)
muda.

### 7.3 Função de exemplo — "usuários ativos que sejam Administrador ou Gestor"

```ts
function montarFiltroAdministradoresOuGestoresAtivos(): FilterNode {
  return {
    logic: 'and',
    children: [
      {
        logic: 'or',
        children: [
          { field: 'perfil', operator: 'eq', value: 'Administrador' },
          { field: 'perfil', operator: 'eq', value: 'Gestor' },
        ],
      },
      { field: 'ativo', value: 'true' },
    ],
  };
}

async function buscarUsuarios(filtro: FilterNode) {
  const response = await fetch('/api/usuarios/busca?sort=-criadoEm&page=1&pageSize=20', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(filtro),
  });
  return response.json();
}
```

---

## 8. Resposta

Este formato **já é o de hoje** — nenhuma das duas propostas altera `PagedResult<T>` nem
`PageMeta`. Confira em `src/Queryable/Core/PagedResult.cs` e `src/Queryable/Core/PageMeta.cs`.

**Já vigente hoje (funciona)**

```json
{
  "items": [
    {
      "id": "3f2e5a1c-...",
      "nome": "Maria Souza",
      "cpf": "12345678900",
      "email": "maria@exemplo.com",
      "ativo": true,
      "criadoEm": "2026-02-10T00:00:00",
      "perfil": "Administrador"
    }
  ],
  "meta": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1,
    "hasPrevious": false,
    "hasNext": false
  }
}
```

`totalPages`, `hasPrevious` e `hasNext` são propriedades calculadas de `PageMeta`
(`TotalPages => PageSize == 0 ? 0 : Ceiling(TotalCount / PageSize)`, `HasPrevious => Page > 1`,
`HasNext => Page < TotalPages`) — não são campos setados manualmente pelo serviço.

Tipo TypeScript correspondente:

```ts
interface PageMeta {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

interface PagedResult<T> {
  items: T[];
  meta: PageMeta;
}

interface UsuarioDto {
  id: string;
  nome: string;
  cpf: string;
  email: string;
  ativo: boolean;
  criadoEm: string; // ISO 8601
  perfil: string;
}
```

---

## 9. Migração

Nada é obrigatório: as duas propostas são **aditivas**. Um projeto que já usa
`Queryable.DynamicFilter` (com ou sem `[Queryable]`) continua funcionando sem qualquer mudança se
não adotar nada abaixo.

Passo a passo opcional, para quem quiser adotar:

1. Criar as `QueryableConfiguration<T>` para as entidades desejadas (ex.:
   `UsuarioQueryConfiguration`), na camada de Infraestrutura.
2. Registrar com `AddQueryableConfigurationsFromAssembly(...)` no `Program.cs`, ao lado de
   `AddQueryableDynamicFilterEfCore()`.
3. Remover os `[Queryable]` do domínio, um tipo por vez, à medida que a configuração fluent
   equivalente para aquele tipo entra em produção.
4. Ligar `OnlyMapped()` tipo a tipo, começando pelas entidades com campo sensível (como
   `SenhaHash` em `Usuario`).
5. Atualizar o frontend de `cpf.value` para `cpf` (e equivalentes para os demais value objects
   colapsados).

**O passo 5 é breaking para o contrato HTTP público** — qualquer cliente que hoje monta
`?cpf.value=...` (ou qualquer alias automático baseado em navegação) para de funcionar assim que
o alias antigo for removido do mapa resolvido, se `OnlyMapped()` estiver ativo, ou continua
funcionando em paralelo se o tipo permanecer em modo permissivo (seção 8 de
`proposta-configuracao-fluent.md`: "a entrada automática `cpf.value` ... continua existindo em
paralelo, a menos que a decisão em aberto opte por removê-la"). Para evitar quebra, sugere-se um
período de convivência: manter o tipo em modo permissivo (sem `OnlyMapped()`) enquanto ambos os
aliases (`cpf` e `cpf.value`) respondem à mesma coluna, e só ativar `OnlyMapped()` — que fecha o
alias antigo, salvo se ele também for declarado explicitamente com `For(...)` — depois que o
frontend tiver migrado e o alias antigo não for mais necessário.

---

## 10. O que NÃO muda

- `QuerySpec<T>.Filters` (`Dictionary<string, string>`) — continua existindo, mesmo tipo, mesmo
  comportamento.
- O model binder de query string (`QuerySpecModelBinder<T>`, incluindo o reconhecimento de `page`,
  `pageSize`, `sort`, `skipTotalCount` e o padrão `Filters[chave]` do Swagger UI).
- `PagedResult<T>` / `PageMeta` — formato de resposta idêntico ao de hoje (seção 8).
- Paginação (`Page`, `PageSize`, `SkipTotalCount`, incluindo a regra de `Page`/`PageSize <= 0`
  serem ignorados silenciosamente).
- `IProjectable<TEntity, TSelf>` — já existe hoje, nenhuma das duas propostas altera a interface.
  Continua sendo um caminho válido (seção 4.2), mas não é o recomendado para arquiteturas em
  camadas com Contracts isolado do Domain — para essas, o caminho recomendado é a projeção
  declarada em Application (seção 4.1).
- A sobrecarga de `IPagedQueryService.ApplyFilterPaginatedAsync` com projeção `Expression`
  explícita — já existe hoje, nenhuma das duas propostas a introduz (seção 4.1).
- O pacote `Queryable.DynamicFilter.EntityFrameworkCore` — consome `Expression<Func<T, bool>>`
  como hoje; a árvore `FilterNode` é interna ao pacote núcleo (`Queryable.DynamicFilter`).
