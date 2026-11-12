---
name: queryable-dynamicfilter
description: Use ao instalar e configurar Queryable.DynamicFilter (e o pacote .EntityFrameworkCore) num projeto ASP.NET Core, ou ao atualizar um projeto que já usa a biblioteca a partir de uma versão anterior.
---

# Skill: Queryable.DynamicFilter — setup inicial e migração

Esta skill é para quem **consome** os pacotes NuGet `Queryable.DynamicFilter` e
`Queryable.DynamicFilter.EntityFrameworkCore` numa API ASP.NET Core. Ela guia dois trabalhos:

- **Setup inicial**: instalar, registrar no DI, escolher o caminho de integração (controller com
  `QuerySpec<T>` ou `RequestQuery` + `IPagedQueryService`), fechar a superfície de exposição e
  tratar os erros como `400`.
- **Migração**: atualizar um projeto que já usa a biblioteca para a versão corrente, revisando
  cada mudança de comportamento entre a versão instalada e a nova.

Detalhes ficam em dois arquivos de referência, carregue só o que a tarefa pedir:

- [`references/setup.md`](references/setup.md) — passo a passo do setup, com código.
- [`references/migracao.md`](references/migracao.md) — mudanças versão a versão (desde `10.1.0`)
  e o que verificar no projeto em cada uma.

A documentação completa e sempre atual é o `README.md` do repositório
(<https://github.com/SamuelGFDias/Queryable>), que também vai empacotado dentro dos dois `.nupkg`.

## Passo 1 — Diagnosticar o projeto

Antes de mexer em qualquer coisa, descubra em que situação o projeto está:

```bash
# Versão instalada (procure nos dois lugares: com gestão central de pacotes a versão fica no props)
grep -rn "Queryable.DynamicFilter" --include=*.csproj --include=Directory.Packages.props --include=packages.lock.json .
dotnet list package | grep -i queryable

# Última versão publicada (os dois pacotes saem sempre juntos, com o mesmo número)
curl -s https://api.nuget.org/v3-flatcontainer/queryable.dynamicfilter/index.json
```

E levante como a biblioteca é usada hoje:

```bash
grep -rnE "AddQueryableDynamicFilter|AddQueryableDynamicFilterEfCore|QuerySpecModelBinderProvider|QuerySpec<|RequestQuery|IPagedQueryService|IQuerySpecApplier|QueryableConfiguration<|\[Queryable" --include=*.cs .
```

Requisitos: os pacotes miram **.NET 10** (`net10.0`) e o pacote EF Core depende de
`Microsoft.EntityFrameworkCore` 10.x. Um projeto em .NET 8/9 precisa subir o target framework antes.

- Nenhuma referência aos pacotes → **setup inicial** (Passo 2A).
- Já referencia → **migração** (Passo 2B). Se, além de atualizar, a pessoa quer adotar recursos
  novos, faça a migração primeiro e o setup do recurso depois.

## Passo 2A — Setup inicial

Siga [`references/setup.md`](references/setup.md). Em resumo:

1. Instalar `Queryable.DynamicFilter` (e `Queryable.DynamicFilter.EntityFrameworkCore` se a fonte
   de dados for EF Core). Não fixe `--version`.
2. Registrar no DI. Com limites customizados e EF Core, a sobrecarga
   `AddQueryableDynamicFilter(limits => ...)` tem que vir **antes** de
   `AddQueryableDynamicFilterEfCore()`, senão os limites são ignorados em silêncio.
3. Escolher o caminho:
   - **Caminho A** — controller recebe `[FromQuery] QuerySpec<T>`; exige registrar
     `QuerySpecModelBinderProvider` no MVC.
   - **Caminho B** (recomendado com EF Core) — controller recebe `RequestQuery` e chama
     `IPagedQueryService.ApplyFilterPaginatedAsync`.
4. **Fechar a superfície exposta.** Por padrão, *toda* propriedade pública da entidade e das
   navegações é filtrável e ordenável; `[Queryable]` só renomeia, não restringe. Para cada entidade
   exposta, pergunte à pessoa se há campo sensível e, se houver, crie uma
   `QueryableConfiguration<TEntity>` com `OnlyMapped()` ou `Ignore(...)`.
5. Tratar erros de entrada como `400` (campo inexistente, valor inválido, limite estourado). Fora
   o que o model binder do Caminho A já converte, essas exceções viram `500` se ninguém as tratar.

## Passo 2B — Migração

Siga [`references/migracao.md`](references/migracao.md):

1. Identifique a versão de origem e a de destino.
2. Percorra **todas** as seções entre as duas, em ordem; cada uma diz o que procurar no código e
   no contrato HTTP. A maioria das versões é aditiva, mas algumas mudam comportamento observável
   pelo cliente da API (formato de número, aliases que somem, `pageSize` máximo, enum numérico,
   chave `filter` reservada).
3. Atualize os **dois** pacotes juntos, para a mesma versão.
4. Liste para a pessoa as mudanças que afetam quem *chama* a API (frontend, integrações): essas
   precisam ser comunicadas, não só corrigidas no código.

Se a versão de destino for mais nova que a última documentada em `references/migracao.md`, leia
as seções novas do `README.md` e as notas das tags no GitHub
(<https://github.com/SamuelGFDias/Queryable/tags>) antes de concluir, e avise a pessoa de que
essa parte não está coberta pela skill.

## Passo 3 — Verificar

1. `dotnet build` sem erro nem aviso novo.
2. Rodar os testes do projeto.
3. Exercitar o endpoint com algumas requisições reais, incluindo erros:
   ```http
   GET /api/produtos?nome__contains=note&sort=-preco&page=1&pageSize=20
   GET /api/produtos?filter=(nome__contains=ana or nome__contains=joao) and ativo=true
   GET /api/produtos?campoQueNaoExiste=1          # deve dar 400, não 500
   GET /api/produtos?pageSize=5000                # deve dar 400 (MaxPageSize = 1000)
   ```
4. Se o projeto tem testes de integração com EF Core, prefira um provider real (SQLite, banco de
   teste) a `List<T>.AsQueryable()`: LINQ-to-Objects executa expressões que o EF Core não
   consegue traduzir para SQL, e um teste em memória pode passar com algo que quebra em produção.

## Armadilhas que valem em qualquer versão

- `Apply` **sempre** ordena: sem `sort`, aplica `OrderBy(x => 0)`, que descarta qualquer
  `OrderBy` feito antes na `query`. Ordenação padrão vai dentro do `afterSpec`, e condicional:
  `afterSpec: q => string.IsNullOrWhiteSpace(request.Sort) ? q.OrderByDescending(p => p.CriadoEm) : q`.
- No Caminho A manual, a ordem é `Apply` → `CountAsync` → `ApplyPaged`. Contar depois de
  `ApplyPaged` dá o tamanho da página, não o total.
- `new FilterBuilder()`/`new SortBuilder()` sem argumentos ignoram toda a configuração fluente.
  Resolva pelo DI.
- Configuração fluente registrada depois do primeiro uso do provider não tem efeito (o mapa é
  cacheado por tipo na primeira consulta). Registre tudo na inicialização.
- `RequestQuery.QueryFilter` com `in` exige `;` na string, mesmo com um filtro só:
  `"categoriaid__in=1,2;"`.
- O namespace raiz é `Queryable`, que sombreia `System.Linq.Queryable`. Se o projeto chama
  `Queryable.OrderBy(...)` por reflexão ou de forma estática, escreva `System.Linq.Queryable`.
