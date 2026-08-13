# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## O que é

`Queryable.DynamicFilter` — biblioteca NuGet (.NET 10) que traduz query string em filtro/ordenação/paginação sobre `IQueryable<T>`, via árvores de expressão. O repositório contém dois pacotes empacotáveis:

- **`Queryable.DynamicFilter`** (`src/Queryable/`) — núcleo: query spec, builders de filtro e ordenação, model binder, sem dependência de Entity Framework.
- **`Queryable.DynamicFilter.EntityFrameworkCore`** (`src/Queryable.EntityFrameworkCore/`) — integração com EF Core: `IPagedQueryService` e `PagedQueryService` para aplicar filtro/ordenação/paginação com projeção em DTO, reusando o núcleo via `ProjectReference`.

Ambos são publicados no NuGet, sem consumidores no repositório.

## Comandos

```bash
dotnet build                                                  # Debug; também gera .nupkg (GeneratePackageOnBuild=true)
dotnet build -c Release
dotnet pack src/Queryable/Queryable.csproj -c Release --no-build --output ./artifacts
dotnet pack src/Queryable.EntityFrameworkCore/Queryable.EntityFrameworkCore.csproj -c Release --no-build --output ./artifacts
```

**`dotnet pack` sozinho falha com `NU5026`** (dll não encontrada). `GeneratePackageOnBuild=true` quebra a ordem build→pack padrão, então sempre rode `dotnet build -c Release` antes e passe `--no-build` no pack. Existem agora dois projetos empacotáveis, ambos precisam de `dotnet pack`. O workflow de publicação faz exatamente isso.

Existem duas suítes xUnit: `tests/Queryable.Tests` (núcleo, sem EF Core) e `tests/Queryable.EntityFrameworkCore.Tests` (EF Core contra SQLite in-memory). Rodam com `dotnet test`. A suíte EF importa especificamente porque `List<T>.AsQueryable()` (LINQ-to-Objects) executa em memória `Expression`s que o EF Core não consegue traduzir para SQL — um teste que só passa pela suíte núcleo pode estar verde com uma expressão que quebra em produção contra um provider real; só a suíte EF pega erro de tradução.

Versões de pacote são centralizadas em `Directory.Packages.props` (`ManagePackageVersionsCentrally`) — `PackageReference` no `.csproj` vai **sem** `Version`.

## Arquitetura

O fluxo completo de uma requisição atravessa cinco arquivos e só faz sentido lido de ponta a ponta:

```
query string
  → QuerySpecModelBinder<T>      (Extensions/) monta QuerySpec<T> a partir de HttpContext.Request.Query
  → QuerySpec<T>                 (Core/) Filters + Sort + Page/PageSize/SkipTotalCount
  → QuerySpecApplier             (Core/) orquestra: Where(predicate) → ApplySort → (Skip/Take)
      ├→ FilterBuilder           (Builders/) query params → Expression<Func<T,bool>>
      └→ SortBuilder             (Builders/) "campo,-campo" → IOrderedQueryable<T>
           ambos resolvem nomes via
  → PathExtension.BuildPropertyPaths<T>()   (Extensions/)
```

**`PathExtension` é a fonte única da verdade sobre o que é endereçável.** Ele reflete `T` recursivamente e devolve `Dictionary<alias, List<PropertyInfo>>` (case-insensitive), onde a lista é a cadeia de propriedades a encadear com `Expression.Property`. Filtro e ordenação compartilham esse mapa — qualquer mudança nele altera os dois de uma vez.

Pontos não óbvios do mapeamento:

- **`[Queryable]` é opcional e não restringe nada.** A checagem `if (attr == null) continue;` está comentada em `src/Queryable/Extensions/PathExtension.cs:25-26`. Hoje **toda propriedade pública** é filtrável/ordenável; o atributo só define um *alias*. O `src/Queryable/README.md` documenta isso como superfície de exposição da API. Se for reintroduzir o opt-in, é ali — e o README precisa acompanhar.
- **Há três guardas contra ciclo/explosão na recursão.** (1) guarda de coleção: propriedade cujo tipo implementa `IEnumerable` (e não é `string`) entra no mapa de aliases, mas a recursão não desce nela — elimina o vetor clássico de recursão infinita em navegação bidirecional de EF (`Produto.Categoria`/`Categoria.Produtos`) e os aliases lixo de `List<T>` (`.capacity`, `.count`, `.item`); (2) guarda de ciclo por caminho: um tipo já presente no caminho atual não é reentrado — **de propósito é por caminho, não global**, porque uma guarda global quebraria ramos irmãos do mesmo tipo (ex.: `Pedido.EnderecoEntrega` e `Pedido.EnderecoCobranca`, ambos `Endereco`, que precisam continuar os dois mapeados); (3) `MaxDepth = 5`, rede de segurança extra independente das outras duas.
- Aliases aninhados usam ponto: `categoria.nome`. Chaves de filtro usam `campo__operador`; sem sufixo, o operador é `eq`.

Outros comportamentos que dependem de ler mais de um arquivo:

- `Apply` **sempre** ordena — sem `sort`, `SortBuilder` devolve `query.OrderBy(x => 0)`. Isso é o que torna `ApplyPaged` (`Skip`/`Take`) legítimo no provider. Não remova esse fallback.
- `ApplyPaged` **não** chama `Apply`. O chamador encadeia os dois na ordem `Apply` → `CountAsync` → `ApplyPaged`, porque o total tem que ser contado sobre o conjunto filtrado e não paginado.
- `QuerySpec.Page`/`PageSize` ignoram atribuições `<= 0` no setter e mantêm o default (1 / 10). Não valide isso de novo na borda.
- **Conversão de valores é única e compartilhada entre operadores.** `ConvertScalar` (privado, em `FilterBuilder.cs`) trata `Guid`, enums, `DateOnly`/`TimeOnly`, `Nullable<>` e o literal `"null"`, e é chamado tanto por `ConvertValue` (operadores escalares) quanto por `BuildInExpression` (`in`). Essa unificação existe justamente para as duas rotinas não voltarem a divergir — ao mexer em conversão de valor, mexa em `ConvertScalar`, nunca duplique lógica em um dos dois chamadores.
- O binder aceita tanto `?nome=ana` quanto a forma `?Filters[nome]=ana` que o Swagger UI gera (regex `SwaggerFilterRegex`), e normaliza as chaves para minúsculas.
- `contains` só é aceito em `string`; qualquer outro tipo cai no `_ => throw NotSupportedException`.

### RequestQuery e camada Entity Framework Core

`RequestQuery` (Core/) é um modelo achatado alternativo para requisições que recebem os parâmetros de filtro, ordenação e paginação como campos simples (ex.: corpo de request, DTO de query string única) em vez do formato posicional baseado em dicionário. Converte-se para `QuerySpec<T>` via `RequestQueryExtensions.ToQuerySpec<T>()`, que então é aplicado normalmente pelo núcleo.

Regra crítica de separador em `QueryFilter`: usa `;` quando a string contém esse caractere, senão `,`. Isso evita colisão com o operador `in`, cujo valor é CSV (ex.: `"id__in=1,2,3;ativo=true"`).

`IPagedQueryService` (Queryable.EntityFrameworkCore) fornece `ApplyFilterPaginatedAsync<TEntity, TDto>` em duas sobrecargas:
- **Com projeção explícita**: `Expression<Func<TEntity, TDto>>` passado pelo chamador.
- **Com `IProjectable`**: `TDto` implementa `IProjectable<TEntity, TDto>` e expõe a projeção como membro estático; resolve em tempo de compilação, sem reflexão.

Por que `IProjectable` mora no núcleo e não no pacote EF? Porque um assembly de DTOs/Contracts pode declarar a projeção sem referenciar Entity Framework, permitindo compartilhamento com clientes que usam apenas o núcleo.

`PagedQueryService` reutiliza `ApplyPaged` e `ToPagedResult` do núcleo em vez de reimplementar Skip/Take, respeita `SkipTotalCount` (omite `COUNT` quando `true`), e aplica `AsNoTracking` automaticamente.

### Colisão de namespace

O namespace raiz é `Queryable`, o que sombreia `System.Linq.Queryable`. `SortBuilder.cs` precisa escrever `typeof(System.Linq.Queryable)` por extenso para invocar `OrderBy`/`ThenBy` por reflexão. Espere esse atrito ao mexer em qualquer código que use LINQ por reflexão.

Da mesma forma, no pacote EF Core o namespace `Queryable.EntityFrameworkCore` colide com `Microsoft.EntityFrameworkCore`. `PagedQueryService.cs` usa `using global::Microsoft.EntityFrameworkCore;` para que extensões como `CountAsync`, `ToListAsync` e `AsNoTracking` resolvam corretamente.

## Publicação no NuGet

`.github/workflows/publish.yml` dispara em tag `v*`: build Release → `dotnet test` (as duas suítes precisam passar) → pack dos DOIS projetos → `NuGet/login@v1` (OIDC, sem secret, user `samueldias21`) → `dotnet nuget push --skip-duplicate`.

Ambos os pacotes (`Queryable.DynamicFilter` e `Queryable.DynamicFilter.EntityFrameworkCore`) compartilham a mesma versão, derivada da tag, então saem sempre em par.

**A versão vem do MinVer, derivada da tag git** — não existe mais `<Version>` no csproj. Publicar é:

```bash
git tag v10.1.1 && git push --tags
```

A última versão **já publicada no nuget.org é `10.1.0`** (o `<Version>` que estava no csproj, `10.0.0`, estava defasado). A primeira tag precisa ser maior que `10.1.0`, senão você publica uma versão que fica atrás da corrente.

Consequências práticas:

- `MinVerTagPrefix` é `v`; sem tag alcançável a versão é `0.0.0-alpha.0.<height>`.
- `MinVerSkip` está ligado em Debug, então `dotnet build` local não paga o custo do MinVer nem produz versão de release.
- O checkout do workflow usa `fetch-depth: 0` porque o MinVer precisa do histórico e das tags. Não reduza isso.
- OIDC depende da policy de *trusted publishing* `queryable_dynamic_filter` no nuget.org (owner `samueldias21` → `SamuelGFDias/Queryable`, workflow `publish.yml`, environment `production`). A policy do `audit_log` não cobre este repo.
- A policy valida o claim `environment`, por isso o job declara `environment: production`. Se um dos dois lados mudar, o outro tem que mudar junto, ou o `NuGet/login@v1` falha na autenticação.
- O `README.md` **não** fixa versão: o comando de instalação é `dotnet add package Queryable.DynamicFilter` (resolve a última) e a versão corrente aparece via badge do shields.io. Não reintroduza `--version` num exemplo — vira documentação que apodrece a cada release. `img.shields.io` e os badges de workflow do GitHub estão na allowlist de imagens do nuget.org, então renderizam nos dois lugares.

## Git

O repositório publica em **dois remotes**: `origin` tem duas push URLs (GitHub `SamuelGFDias/Queryable` + GitLab `ses-rj/desenvolvimento/libs/dotnet/queryable`), então um `git push` normal envia para os dois. O `fetch` vem do GitHub; o remote extra `gitlab` existe para buscar o lado do GitLab. Tags também vão para os dois — só o GitHub Actions reage a elas.

O CI do GitLab (`.gitlab-ci.yml`) delega ao pipeline compartilhado `ses-rj/ci-cd/pipelines` (`simple-ci/manifests/v1/v1.yml`) e não tem configuração local.

Mensagens de commit seguem `AGENTS.md`: `<ícone> Queryable - <categoria>: <descrição>` em português, com categorias fixas (`feat` ✨, `bugfix` 🐛, `refactor` ♻️, `remove` 🔥, `chore` 🧹, `docs` 📚, `test` 🧪, `build` ⚙️, `ci` 🔧, `deps` 📦, `perf` 🚀, `style` 🎨). Até `v10.3.0` (commits até `77e1f07`), `AGENTS.md` grafava o prefixo `Qeryable` (sem o "u") e o histórico é inconsistente entre `Qeryable` e `Queryable`; a partir daqui `AGENTS.md` foi corrigido para `Queryable` e commits novos devem usar essa grafia. O histórico antigo não foi reescrito.

## Repomix

`repomix.config.json` empacota o repo em Markdown para consumo por LLM. `.repomixignore` ainda exclui `Api/` (projeto já removido) e `Queryable.slnx`. `repomix-output.xml` é um artefato versionado e desatualizado (formato XML antigo, anterior ao move para `src/`); a config atual gera `repomix-output.md`, que é gitignorado.

## Propostas em aberto

`docs/proposta-filtros-compostos.md` é uma **proposta para avaliação** (filtros compostos: OR, agrupamento, NOT), **nada dela foi implementado**. O comportamento atual (`Filters` como `Dictionary<string,string>`, combinação exclusiva por `AND`) permanece o real. Não confunda o que esse documento descreve com o estado atual do código.
