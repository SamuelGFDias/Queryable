# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## O que é

`Queryable.DynamicFilter` — biblioteca NuGet (.NET 10) que traduz query string em filtro/ordenação/paginação sobre `IQueryable<T>`, via árvores de expressão. Projeto único em `src/Queryable/`, sem consumidores no repo.

## Comandos

```bash
dotnet build                                                  # Debug; também gera .nupkg (GeneratePackageOnBuild=true)
dotnet build -c Release
dotnet pack src/Queryable/Queryable.csproj -c Release --no-build --output ./artifacts
```

**`dotnet pack` sozinho falha com `NU5026`** (dll não encontrada). `GeneratePackageOnBuild=true` quebra a ordem build→pack padrão, então sempre rode `dotnet build -c Release` antes e passe `--no-build` no pack. O workflow de publicação faz exatamente isso.

Não existe projeto de teste — `dotnet test` não roda nada. Ao alterar `FilterBuilder`/`SortBuilder`/`PathExtension`, valide manualmente: as regressões aqui são silenciosas em compilação e só aparecem como exceção em runtime, na primeira requisição.

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

- **`[Queryable]` é opcional e não restringe nada.** A checagem `if (attr == null) continue;` está comentada em `src/Queryable/Extensions/PathExtension.cs:25-26`. Hoje **toda propriedade pública** é filtrável/ordenável; o atributo só define um *alias*. O `README.md` ainda descreve o comportamento antigo ("use `[Queryable]` para expor campos") e está desatualizado nesse ponto. Se for reintroduzir o opt-in, é ali — e o README precisa acompanhar.
- **Não há proteção contra ciclos.** A recursão desce em toda `prop.PropertyType.IsClass` que não seja `string`. Uma navegação bidirecional de EF (`Produto.Categoria.Produtos`) causa recursão infinita. É a limitação estrutural mais séria da lib.
- Aliases aninhados usam ponto: `categoria.nome`. Chaves de filtro usam `campo__operador`; sem sufixo, o operador é `eq`.

Outros comportamentos que dependem de ler mais de um arquivo:

- `Apply` **sempre** ordena — sem `sort`, `SortBuilder` devolve `query.OrderBy(x => 0)`. Isso é o que torna `ApplyPaged` (`Skip`/`Take`) legítimo no provider. Não remova esse fallback.
- `ApplyPaged` **não** chama `Apply`. O chamador encadeia os dois na ordem `Apply` → `CountAsync` → `ApplyPaged`, porque o total tem que ser contado sobre o conjunto filtrado e não paginado.
- `QuerySpec.Page`/`PageSize` ignoram atribuições `<= 0` no setter e mantêm o default (1 / 10). Não valide isso de novo na borda.
- **Conversão de valores diverge entre operadores.** `ConvertValue` trata `Guid`, enums, `DateOnly`/`TimeOnly`, `Nullable<>` e o literal `"null"`; já `BuildInExpression` usa só `Convert.ChangeType`, então `in` quebra para `Guid`/enum — inclusive no exemplo `id__in=<guid>` do README. Se for corrigir, é fazer `in` reusar `ConvertValue`.
- O binder aceita tanto `?nome=ana` quanto a forma `?Filters[nome]=ana` que o Swagger UI gera (regex `SwaggerFilterRegex`), e normaliza as chaves para minúsculas.
- `contains` só é aceito em `string`; qualquer outro tipo cai no `_ => throw NotSupportedException`.

### Colisão de namespace

O namespace raiz é `Queryable`, o que sombreia `System.Linq.Queryable`. `SortBuilder.cs` precisa escrever `typeof(System.Linq.Queryable)` por extenso para invocar `OrderBy`/`ThenBy` por reflexão. Espere esse atrito ao mexer em qualquer código que use LINQ por reflexão.

## Publicação no NuGet

`.github/workflows/publish.yml` dispara em tag `v*`: build Release → pack → `NuGet/login@v1` (OIDC, sem secret, user `samueldias21`) → `dotnet nuget push --skip-duplicate`.

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

Mensagens de commit seguem `AGENTS.md`: `<ícone> Qeryable - <categoria>: <descrição>` em português, com categorias fixas (`feat` ✨, `bugfix` 🐛, `refactor` ♻️, `remove` 🔥, `chore` 🧹, `docs` 📚, `test` 🧪, `build` ⚙️, `ci` 🔧, `deps` 📦, `perf` 🚀, `style` 🎨). O prefixo em `AGENTS.md` está literalmente escrito `Qeryable` (sem o "u") e o histórico é inconsistente entre `Qeryable` e `Queryable`.

## Repomix

`repomix.config.json` empacota o repo em Markdown para consumo por LLM. `.repomixignore` ainda exclui `Api/` (projeto já removido) e `Queryable.slnx`. `repomix-output.xml` é um artefato versionado e desatualizado (formato XML antigo, anterior ao move para `src/`); a config atual gera `repomix-output.md`, que é gitignorado.
