# Plano de implementação

> Plano de execução em etapas para aplicar as melhorias descritas em
> `docs/proposta-filtros-compostos.md` (proposta A) e
> `docs/proposta-configuracao-fluent.md` (proposta B). Este documento é **de planejamento** —
> não implementa nada, não altera código.

---

## 1. Resumo

Este plano organiza a implementação das duas propostas em sete etapas versionadas (`Etapa 0` a
`Etapa 6`), cada uma delimitando um recorte de trabalho fechado. A régua que guia a divisão é
simples: **cada etapa é uma entrega versionada, publicada no NuGet, e reversível** — se algo der
errado depois de uma etapa ir ao ar, o passo seguinte é publicar uma correção (patch) sobre a
tag daquela etapa, não desfazer trabalho em código não publicado. Isso implica que nenhuma etapa
deve deixar o repositório em um estado publicável quebrado, e que a ordem entre etapas é ditada
por dependência técnica real (uma altera o que a outra precisa consumir), não por conveniência de
cronograma.

O plano cobre da consolidação do que já existe em `main` (Etapa 0) até os limites de segurança e
observabilidade da proposta de filtros compostos (Etapa 6), passando pelo provider de caminhos
com cache (Etapa 1), a árvore de filtro (Etapa 2), a configuração fluent (Etapa 3) e as duas
portas de entrada novas — JSON (Etapa 4) e mini-linguagem (Etapa 5).

---

## 2. Estado atual

Confirmado por `git log --oneline -16`, `git status --short`, `git branch -vv` e leitura direta
dos arquivos citados:

- **Última versão publicada no nuget.org: `10.1.0`.** Registrado em `CLAUDE.md`, seção
  "Publicação no NuGet".
- **`main` local está 16 commits à frente de `origin/main`** (`git branch -vv` reporta
  `[origin/main: ahead 16]`). Nenhum desses commits foi publicado. Conteúdo, do mais antigo ao
  mais novo: mover projeto para `src/`, derivar versão via MinVer a partir de tag `v*`, workflow
  de publicação no NuGet, `CLAUDE.md` e badges dinâmicos, `RequestQuery`/`IProjectable`/extensão
  de DI no núcleo, criação do pacote `Queryable.DynamicFilter.EntityFrameworkCore`, publicação do
  pacote EF no workflow, reescrita do README como manual de uso, `CLAUDE.md` atualizado para a
  arquitetura de dois pacotes, correção de conversão de valores no `FilterBuilder`
  (`f9ea2c6`), guardas de ciclo/coleção/profundidade (`9cedd34`), EF Core 10.0.11 e dependências
  de teste, suítes de teste do núcleo e do EF Core, `dotnet test` no workflow de publicação, e
  documentação atualizada após as correções.
- **O que falta para publicar:** o workflow `.github/workflows/publish.yml` declara
  `environment: production` (exigido pela policy de *trusted publishing* do nuget.org, que valida
  o claim `environment` no token OIDC — ver `CLAUDE.md`, seção "Publicação no NuGet"). O
  `environment` `production` **não está confirmado como existente** no repositório GitHub; sem
  ele, o step `NuGet/login@v1` falha na autenticação. Precisa ser criado (Settings → Environments)
  antes da primeira tag pós-`10.1.0`.
- **Duas pendências de vitrine, confirmadas por leitura:** não existe `README.md` na raiz do
  repositório (só `src/Queryable/README.md`, referenciado via `PackageReadmeFile` no
  `Queryable.csproj`); e `src/Queryable.EntityFrameworkCore/Queryable.EntityFrameworkCore.csproj`
  **não** define `PackageReadmeFile` nem inclui nenhum `README.md` — o pacote
  `Queryable.DynamicFilter.EntityFrameworkCore` sobe ao nuget.org sem README.
- **Ponto de colisão confirmado por leitura de `src/Queryable/Builders/FilterBuilder.cs` e
  `src/Queryable/Builders/SortBuilder.cs`:** ambos os builders chamam
  `PathExtension.BuildPropertyPaths<T>()` diretamente no corpo do método público (linha 19 de
  `FilterBuilder.cs`, linha 20 de `SortBuilder.cs`), sem nenhum cache. A fase 1 da proposta B
  substitui essa chamada por um `IPropertyPathProvider` injetado via construtor. Já a fase 1 da
  proposta A reescreve o restante do corpo de `BuildPredicate` (linhas 21-52 de
  `FilterBuilder.cs`, o laço `foreach` que monta `Expression.AndAlso`) para delegar a um
  compilador `FilterNode → Expression`. As duas mudanças tocam o mesmo arquivo,
  `FilterBuilder.cs`, em regiões diferentes mas adjacentes (construtor + linha 19 vs. corpo do
  método) — ver seção 4 para o motivo de precisarem ser sequenciais.
- `docs/proposta-configuracao-fluent.md` está **sem commit** (`git status --short` reporta `??`),
  assim como `docs/exemplos-uso-propostas.md`. Este plano trata o conteúdo de ambas as propostas
  como insumo de leitura, independentemente do estado de versionamento do arquivo — não é escopo
  deste documento alterar isso.

---

## 3. As etapas

### Etapa 0 — Consolidar o que já existe

| | |
|---|---|
| **Versão alvo** | `v10.2.0` |
| **Depende de** | nada (é o ponto de partida) |
| **Risco para o consumidor** | nenhum (é o próprio código já escrito, ainda não publicado) |

**Objetivo.** Publicar no nuget.org tudo que já está pronto em `main` local mas nunca foi
liberado.

**Conteúdo.** Os 16 commits descritos na seção 2: separação em dois pacotes
(`Queryable.DynamicFilter` + `Queryable.DynamicFilter.EntityFrameworkCore`), MinVer, workflow de
publicação, suíte de testes (núcleo + EF Core), e as três correções de bug (conversão de valores
dependente de cultura, guardas de ciclo/coleção/profundidade em `PathExtension`, e o que motivou
a correção do operador `in` com `Guid`/enum). Opcionalmente, as duas pendências de vitrine da
seção 2: `README.md` na raiz do repositório e `README.md` para o pacote EF Core (com
`PackageReadmeFile` no `.csproj` correspondente).

**Justificativa do salto de minor (`10.1.0` → `10.2.0`, não patch).** Há pacote novo
(`Queryable.DynamicFilter.EntityFrameworkCore` nunca publicado) e APIs novas no núcleo
(`RequestQuery`, `IProjectable`, extensões de DI) somadas às correções de bug — um conjunto que
não é "só correção", é aditivo em superfície pública.

**Ações.**

1. `git push` dos 16 commits para `origin` (GitHub + GitLab, via as duas push URLs do remote).
2. Criar o environment `production` no repositório GitHub (Settings → Environments) — a policy de
   *trusted publishing* do nuget.org valida esse claim no token OIDC; sem ele, `NuGet/login@v1`
   falha.
3. `git tag v10.2.0 && git push --tags`.

**Critério de pronto.** Os dois pacotes aparecem no nuget.org na versão `10.2.0`; o workflow
`publish.yml` roda do início ao fim sem falhar no login.

**Bloqueio conhecido.** Sem o environment `production` criado previamente, o step
`NuGet/login@v1` falha e a tag fica "gasta" sem publicar (MinVer deriva versão da tag mais alta
alcançável; uma tag falha exige nova tag de patch para tentar de novo, não republicar a mesma).

---

### Etapa 1 — Provider de caminhos + cache

| | |
|---|---|
| **Versão alvo** | `v10.3.0` |
| **Depende de** | Etapa 0 |
| **Risco para o consumidor** | nenhum (zero mudança de API observável) |

**Objetivo.** Eliminar a varredura por reflexão duplicada entre `FilterBuilder` e `SortBuilder` e
abrir o ponto de extensão que a Etapa 3 vai usar.

**Conteúdo.** Proposta B, fase 1 (seção 11 daquele documento): interface `IPropertyPathProvider`,
implementação padrão só-reflexão (delega a `PathExtension.BuildPropertyPaths<T>()`, que
**continua `public static`**, inalterada), cache por tipo em
`ConcurrentDictionary<Type, IReadOnlyDictionary<string, List<PropertyInfo>>>`. `FilterBuilder` e
`SortBuilder` passam a receber o provider por construtor, **preservando o construtor sem
parâmetros** (usado hoje em `tests/Queryable.Tests/FilterBuilderTests.cs` e
`tests/Queryable.Tests/SortBuilderTests.cs`, e potencialmente por consumidores externos fora do
container de DI). `AddQueryableDynamicFilter()` passa a registrar `IPropertyPathProvider` como
`TryAddSingleton`.

**Ganho.** Hoje são duas varreduras recursivas por reflexão a cada requisição que filtra e ordena
(uma em `FilterBuilder.cs:19`, outra em `SortBuilder.cs:20`), sem nenhum cache em nenhum dos dois
lugares. Com o provider cacheado, a segunda varredura na mesma requisição é eliminada — ganho de
performance puro, sem exigir nenhuma mudança de código do consumidor.

**Por que é a primeira etapa de código.** Menor superfície entre as seis etapas de código, ganho
imediato mensurável, e não depende de nenhuma decisão de design ainda em aberto (as questões
abertas da proposta B — seção 14 daquele documento — são todas sobre a configuração fluent da
Etapa 3, nenhuma sobre o provider em si).

**Critério de pronto.** `IPropertyPathProvider` existe e é consumido por ambos os builders;
`new FilterBuilder()` / `new SortBuilder()` continuam compilando e passando nos testes
existentes; um teste de cache (chamar `GetPaths<T>()` duas vezes e confirmar que a segunda não
refaz a varredura) está verde.

---

### Etapa 2 — Árvore de filtro

| | |
|---|---|
| **Versão alvo** | `v10.4.0` |
| **Depende de** | Etapa 1 (sequencial, não paralelizável — ver seção 4) |
| **Risco para o consumidor** | nenhum (API pública ainda não muda) |

**Objetivo.** Estabelecer a árvore `FilterNode` como representação intermediária única do motor
de filtro, sem ainda expor nenhuma porta de entrada nova.

**Conteúdo.** Proposta A, fase 1 (seção 9 daquele documento): records `FilterNode` (abstrato),
`FilterCondition`, `FilterGroup` (com `FilterLogic.And`/`Or`), `FilterNot`; o adaptador
`Dictionary<string,string> → FilterGroup(And, ...)` (preservando o caso especial de dicionário
vazio ⇒ `x => true`); o compilador recursivo `FilterNode → Expression`, reaproveitando
`ParseKey`/`ConvertValue`/`ConvertScalar`/`BuildInExpression` já existentes em `FilterBuilder.cs`
sem alterar essas rotinas. `FilterBuilder.BuildPredicate<T>(IDictionary<string,string>)` passa a
delegar internamente para adaptador + compilador — assinatura e resultado observável idênticos.
`IFilterBuilder` ganha `BuildPredicate<T>(FilterNode)` como *default interface method* (seção 6.1
da proposta), preservando qualquer implementação externa de `IFilterBuilder`.

**Invisível para o consumidor.** A API pública não muda ainda nesta etapa — o ganho é estrutural
(base testável isoladamente, compilador exercitado com árvores montadas manualmente em código,
sem depender de nenhum parser ainda inexistente) e prepara o terreno para as Etapas 4 e 5.

**Por que vem depois da Etapa 1, e não em paralelo.** Ver seção 4 — colisão em
`FilterBuilder.cs`.

**Critério de pronto.** `FilterBuilder.BuildPredicate<T>(IDictionary<string,string>)` produz,
para qualquer entrada, o mesmo `Expression` que produzia antes da etapa (testes de regressão da
suíte núcleo continuam verdes sem alteração); testes novos do compilador (árvore → `Expression`)
cobrindo `AND`/`OR`/`NOT` estão verdes, incluindo a camada de tradução real via SQLite in-memory
(seção 10 da proposta A, camada 3) para pelo menos os casos com `OR` atravessando navegação e
`NOT`.

---

### Etapa 3 — Configuração fluent

| | |
|---|---|
| **Versão alvo** | `v10.5.0` |
| **Depende de** | Etapa 1 |
| **Risco para o consumidor** | aditivo (modo permissivo); a ativação de `OnlyMapped()` é opt-in por tipo e é a que introduz risco — ver seção 8 |

**Objetivo.** Permitir renomear/colapsar caminhos (ex.: `Usuario.Cpf.Value` → `cpf`) e restringir
o que é endereçável por tipo, sem anotação no domínio.

**Conteúdo.** Proposta B, fases 2 e 3 (seção 11 daquele documento): classe base
`QueryableConfiguration<TEntity>` com `For(...)`/`Ignore(...)`/`OnlyMapped()`,
`QueryablePropertyBuilder<TEntity>.As(alias)`, o algoritmo de extração de
`Expression<Func<TEntity,TProperty>> → List<PropertyInfo>` (desembrulhando `UnaryExpression`,
rejeitando chamada de método/indexador/campo com erro citando a expressão), registro via
`AddQueryableConfigurationsFromAssembly(Assembly)` / `AddQueryableConfiguration<TConfiguration>()`.
Primeiro o modo permissivo (configurado sobrescreve automático, demais aliases automáticos
intactos), depois `OnlyMapped()` para o modo opt-in (só entradas declaradas existem para aquele
tipo).

**Depende da Etapa 1** porque o provider (`IPropertyPathProvider`) é o ponto de resolução onde a
configuração fluent entra — a fase 2 da proposta B aplica a configuração por cima do mapa
construído pelo provider padrão (seção 7 da proposta B). Não depende da Etapa 2: as duas
propostas são ortogonais nesse ponto (seção 10 da proposta B) — a árvore de filtro resolve `Field`
contra o mesmo mapa de caminhos, seja ele vindo do provider puro ou do provider com configuração
fluent aplicada, sem mudança de contrato.

**Critério de pronto.** Testes de extração de caminho (seletor de um nível, seletor com VO
aninhado, casos de rejeição), de mesclagem/precedência em modo permissivo, de modo opt-in, e o
teste de tradução real via SQLite in-memory com `?cpf=...` resolvendo para `Cpf.Value` (seção 12
da proposta B) — todos verdes.

---

### Etapa 4 — Porta JSON

| | |
|---|---|
| **Versão alvo** | `v10.6.0` |
| **Depende de** | Etapa 2 |
| **Risco para o consumidor** | aditivo |

**Objetivo.** Habilitar OR/agrupamento/NOT para clientes que montam o filtro programaticamente
(frontend, integrações) via corpo de requisição POST.

**Conteúdo.** Proposta A, fase 2 (seção 9 daquele documento): `QuerySpec<T>.Filter` (tipo
`FilterNode?`, padrão `null`), `JsonConverter<FilterNode>` polimórfico resolvendo o tipo do nó por
presença de campo (`field` ⇒ condição, `logic`+`children` ⇒ grupo, `not` ⇒ negação, sem
discriminador `$type` explícito), atalho de condição-folha no nível raiz do body sendo tratada
como `FilterGroup(And, [condição])`. Regra de combinação quando `Filters` (legado) e `Filter`
(árvore) são usados juntos: `Filter == null` ⇒ comportamento idêntico ao atual; ambos preenchidos
⇒ `AND(adaptador(Filters), Filter)`.

**Depende da Etapa 2** porque consome diretamente a árvore `FilterNode` e o compilador
introduzidos ali — não há parser a escrever aqui, só desserialização direta para os records já
existentes.

**Critério de pronto.** Testes de desserialização JSON → `FilterNode` para os três formatos de nó
(condição, grupo, negação) e o atalho de condição-folha; teste de combinação `Filters` + `Filter`
simultâneos.

---

### Etapa 5 — Mini-linguagem

| | |
|---|---|
| **Versão alvo** | `v10.7.0` |
| **Depende de** | Etapa 2 |
| **Risco para o consumidor** | aditivo |

**Objetivo.** Habilitar OR/agrupamento/NOT em GET/query string, onde uma árvore JSON seria pouco
natural.

**Conteúdo.** Proposta A, fase 3 (seção 9 daquele documento): tokenizer + parser recursivo
descendente para a gramática da seção 4 daquele documento (precedência `not` > `and` > `or`,
regra de aspas/escape para valores com caracteres especiais, listas `in` delimitadas por
parênteses substituindo a ambiguidade de vírgula do `RequestQuery.QueryFilter` atual); nova
propriedade em `RequestQuery` (nome a definir — ver seção 8); reconhecimento pelo
`QuerySpecModelBinder<T>` para a query string.

**Depende da Etapa 2** pelo mesmo motivo da Etapa 4 (produz uma `FilterNode`, que é compilada pelo
compilador introduzido na Etapa 2), mas **não depende da Etapa 4** — as duas portas (JSON e
mini-linguagem) alimentam a mesma árvore de forma independente uma da outra.

**Critério de pronto.** Testes puramente sintáticos do parser cobrindo a tabela de exemplos
válidos/inválidos da seção 4 da proposta A (incluindo os casos de erro: parêntese não fechado,
aspas não fechadas, palavra-chave colidindo com valor não citado, `in` fora de parênteses).

---

### Etapa 6 — Limites e observabilidade

| | |
|---|---|
| **Versão alvo** | `v10.8.0` |
| **Depende de** | Etapas 4 e 5 |
| **Risco para o consumidor** | aditivo, mas com efeito prático de rejeitar entradas antes aceitas sem limite — requisições que hoje montam árvores muito profundas/largas passam a receber 400 |

**Objetivo.** Impedir que uma árvore de filtro vinda de cliente externo vire um vetor de negação
de serviço (tempo de montagem de `Expression`, uso de memória, `WHERE` patológico no banco).

**Conteúdo.** Proposta A, fase 4 (seção 9 e seção 7 daquele documento): tetos configuráveis
(default sugerido entre parênteses) de profundidade de aninhamento de grupos/`not` (6),
número total de nós na árvore (100), tamanho máximo da string de entrada da mini-linguagem
(4096 caracteres), e quantidade máxima de itens em uma lista `in` (200) — todos configuráveis via
opções registradas no DI. Validação **antes** da compilação para `Expression` (logo após o parse
ou a desserialização), retornando erro de requisição inválida (400) quando um limite é excedido.
Métricas/logs de contagem de nós, profundidade e tempo de compilação, para acompanhar uso real
antes de liberar `OR`/agrupamento em endpoints de maior volume (seção 8 da proposta A — impacto
em performance de `OR`).

**Depende das Etapas 4 e 5** porque os limites se aplicam às duas portas de entrada (JSON e
mini-linguagem) simultaneamente — não faz sentido aplicar um teto de profundidade só a uma das
duas portas.

**Critério de pronto.** Testes confirmando 400 para cada limite excedido (profundidade, número de
nós, tamanho de string, itens de `in`), tanto vindo da porta JSON quanto da mini-linguagem;
validação comprovadamente ocorre antes de qualquer tentativa de compilação para `Expression`
(nenhum teste deve conseguir observar o banco sendo consultado com uma árvore acima do limite).

---

## 4. Grafo de dependências

```
Etapa 0 ──► Etapa 1 ──► Etapa 2 ──┬──► Etapa 3
(v10.2.0)   (v10.3.0)   (v10.4.0) │    (v10.5.0)
                                   │
                                   ├──► Etapa 4 ──┐
                                   │    (v10.6.0) │
                                   │               ├──► Etapa 6
                                   └──► Etapa 5 ──┘    (v10.8.0)
                                        (v10.7.0)
```

**Caminho crítico:** `0 → 1 → 2 → (3 ∥ 4) → 5 → 6`. Em termos de dependência estrita, o que a
Etapa 6 exige é `4` e `5` prontas; a Etapa 3 depende só de `1` e não bloqueia nem é bloqueada por
`4`/`5`. Ou seja, a partir da Etapa 2, três ramos se abrem (`3`, `4`, `5`), todos só exigindo `2`
como pré-requisito comum — a única ordem estritamente linear obrigatória é `0 → 1 → 2`, e depois
`4`+`5 → 6`.

**Por que 1 e 2 NÃO paralelizam, apesar de serem tecnicamente "independentes" em termos de
design.** As duas alteram o mesmo arquivo, `src/Queryable/Builders/FilterBuilder.cs` (e, em menor
grau, `SortBuilder.cs` para a Etapa 1):

- A Etapa 1 (proposta B, fase 1) muda o **construtor** de `FilterBuilder`/`SortBuilder` e a linha
  que obtém o mapa de caminhos (`FilterBuilder.cs:19`, `SortBuilder.cs:20`) —
  `PathExtension.BuildPropertyPaths<T>()` chamado direto vira `_pathProvider.GetPaths<T>()`
  injetado.
- A Etapa 2 (proposta A, fase 1) reescreve o **corpo** de `BuildPredicate` (`FilterBuilder.cs:21`
  a `:52`, o `foreach` que monta `Expression.AndAlso`) para delegar a adaptador + compilador de
  árvore.

Um branch que faz a Etapa 2 primeiro escreveria o novo corpo de `BuildPredicate` ainda chamando
`PathExtension.BuildPropertyPaths<T>()` diretamente (porque o provider da Etapa 1 não existiria
ainda nesse branch); quando esse branch fosse mesclado com o da Etapa 1, o merge não teria como
resolver automaticamente qual das duas versões da linha 19 (chamada direta vs. injeção por
construtor) deveria "vencer" sem edição manual guiada por quem entende as duas mudanças — um
conflito de merge textual, na melhor hipótese, ou pior, um merge que compila mas silenciosamente
descarta uma das duas mudanças. **Isolar cada uma em um worktree próprio não resolve isso — só
adia o conflito do momento de escrever código para o momento de integrar os dois branches.** Por
isso a única forma segura é sequencial: terminar, mesclar em `main` e taggear a Etapa 1 antes de
começar a Etapa 2.

---

## 5. Estratégia de paralelismo com worktrees

Etapas que não têm dependência direta entre si — e portanto não tocam os mesmos arquivos-fonte
enquanto em desenvolvimento — podem ser trabalhadas em paralelo, cada uma em seu próprio branch
(`feat/etapa-N-<slug>`, ex.: `feat/etapa-4-porta-json`, `feat/etapa-5-mini-linguagem`) e seu
próprio `git worktree`.

**Regras.**

1. **Dois trabalhos nunca editam o mesmo arquivo ao mesmo tempo.** Antes de abrir um worktree
   para uma etapa, confirmar que o conjunto de arquivos que ela toca (conforme a seção "Conteúdo"
   de cada etapa acima) não intersecta o conjunto de arquivos que outra etapa em andamento está
   tocando. Quando há qualquer superposição de arquivo — mesmo que as mudanças pareçam
   logicamente independentes — as etapas são sequenciais, não paralelas (é exatamente o caso das
   Etapas 1 e 2, seção 4).
2. **Worktree só enxerga o que está commitado.** Um `git worktree add` cria uma cópia de trabalho
   a partir de um commit/branch existente no repositório — não existe "worktree a partir do que
   está só na working tree de outro worktree". Isso significa que, para uma etapa começar em
   worktree isolado, o estado-base de que ela depende (a etapa anterior no grafo da seção 4)
   precisa estar **commitado e mesclado em `main`** primeiro, não apenas "pronto localmente" em
   outro worktree não mesclado.
3. Cada worktree fecha com o gate da seção 7 rodado localmente antes de abrir o merge para `main`.

**Pares que realmente podem correr juntos:** **Etapa 3 e Etapa 4.** Ambas dependem só de etapas já
mescladas em `main` (Etapa 1 e Etapa 2, respectivamente) e tocam conjuntos de arquivos disjuntos —
a Etapa 3 concentra-se em `QueryableConfiguration<T>`/`QueryablePropertyBuilder<T>`/extensões de
DI (arquivos novos, mais o consumo de `IPropertyPathProvider` já existente da Etapa 1); a Etapa 4
concentra-se em `QuerySpec<T>.Filter` e o `JsonConverter<FilterNode>` (arquivos novos, mais o
consumo do compilador já existente da Etapa 2). Nenhuma edita `FilterBuilder.cs`/`SortBuilder.cs`
de forma que colida com a outra.

**Pares que não podem correr juntos:**

- **Etapa 1 e Etapa 2** — colisão em `FilterBuilder.cs`/`SortBuilder.cs`, detalhada na seção 4.
- **Etapa 4/5 e Etapa 6** — a Etapa 6 aplica validação de limites sobre as árvores produzidas
  pelas duas portas; não há o que paralelizar até as duas existirem.
- Qualquer etapa e sua predecessora direta no grafo — por definição de dependência, não é
  paralelismo, é ordem.

A Etapa 5 (mini-linguagem), embora dependa só da Etapa 2 como a Etapa 4, concentra a maior parte
do trabalho em um parser novo e isolado (tokenizer + parser recursivo descendente) mais o
reconhecimento no `QuerySpecModelBinder<T>` — poderia em princípio correr junto com a Etapa 4
também, desde que se confirme, no momento de abrir os dois worktrees, que nenhuma das duas altera
`QuerySpecModelBinder<T>` da mesma forma simultaneamente (a Etapa 4 não deveria precisar tocar
esse arquivo; se a implementação real mostrar que toca, o par vira sequencial).

---

## 6. Versionamento e release

- **A versão vem do MinVer, derivada da tag `v*`.** Não existe `<Version>` nos `.csproj` — cada
  tag `vX.Y.Z` empurrada define a versão que o `dotnet pack` produz para os dois pacotes na mesma
  execução do workflow (`MinVerTagPrefix` = `v`, `fetch-depth: 0` no checkout do workflow para o
  MinVer enxergar o histórico completo).
- **Cada etapa fecha com merge em `main`, gate completo (seção 7) e tag.** Nenhuma etapa é
  considerada concluída enquanto não estiver mesclada em `main`, com o gate rodado sobre o estado
  pós-merge (não só sobre o branch isolado), e taggeada.
- **Os dois pacotes saem sempre em par, com a mesma versão.** O workflow empacota
  `Queryable.DynamicFilter` e `Queryable.DynamicFilter.EntityFrameworkCore` na mesma execução, a
  partir da mesma tag — não existe tag que publique só um dos dois.
- **Correção dentro de uma etapa já publicada vira patch.** Se, depois de `v10.4.0` (Etapa 2)
  publicada, aparecer um bug no compilador de árvore, a correção é uma tag `v10.4.1`, não uma
  reabertura da Etapa 2 nem uma alteração retroativa no plano — o número de etapa e a versão minor
  correspondente já são história publicada.
- A numeração de minor deste plano (`10.2.0` a `10.8.0`) assume que nenhuma etapa introduz
  mudança que exigiria major sob SemVer (nenhuma remove ou quebra um membro público existente,
  conforme as tabelas de compatibilidade das duas propostas — seção 6 da proposta A, seção 9 da
  proposta B). Se, na implementação real de alguma etapa, uma decisão em aberto (seção 8 deste
  plano) for resolvida de um jeito que quebre compatibilidade, essa etapa passa a exigir major, e
  a numeração das etapas seguintes desloca de acordo.

---

## 7. Gate por etapa

Checklist único, aplicável a toda etapa (0 a 6) antes de criar a tag correspondente:

- [ ] Build da solução em Release com **0 avisos** (`dotnet build -c Release`).
- [ ] `dotnet test` com as duas suítes passando (`tests/Queryable.Tests` e
      `tests/Queryable.EntityFrameworkCore.Tests`) — a suíte EF Core especificamente, porque
      `List<T>.AsQueryable()` executa em memória `Expression`s que o EF Core pode não conseguir
      traduzir para SQL; um teste que só passa pela suíte núcleo não é suficiente para validar
      qualquer etapa que gere `Expression` nova (Etapas 2, 4, 5, 6 em especial).
- [ ] `dotnet pack` gerando os **dois** pacotes sem erro
      (`dotnet pack src/Queryable/Queryable.csproj -c Release --no-build` e o equivalente para
      `src/Queryable.EntityFrameworkCore/Queryable.EntityFrameworkCore.csproj`; lembrar que
      `dotnet pack` sozinho falha com `NU5026` sem o `dotnet build -c Release` anterior).
- [ ] Documentação atualizada: README do(s) pacote(s) afetado(s) pela etapa, e `CLAUDE.md` (em
      particular a seção "Propostas em aberto", que precisa deixar de descrever como "proposta,
      nada implementado" qualquer parte que a etapa tenha efetivamente implementado).
- [ ] Nenhuma afirmação obsoleta na documentação — nenhum trecho de README ou `CLAUDE.md`
      contradiz o comportamento real do código depois da etapa (ex.: depois da Etapa 2, o
      `CLAUDE.md` não pode mais dizer que "`Filters` combina exclusivamente por `AND` sem caminho
      para `OR`" como uma limitação estrutural — passa a ser uma limitação só do formato
      `Dictionary`, não do motor).

---

## 8. Riscos e pontos de decisão

Itens que dependem de decisão humana antes (ou durante) a etapa correspondente, extraídos das
seções "Questões em aberto" das duas propostas:

| Decisão pendente | Onde está descrita | Afeta a etapa |
|---|---|---|
| Nome do parâmetro/propriedade novos para a árvore de filtro (`filter` na query string, `Filter` em `RequestQuery`/`QuerySpec<T>`) — checar colisão com uso existente em consumidores | Proposta A, seção 12 | Etapas 4 e 5 |
| `NOT` entra já na mini-linguagem (fase 3 original) ou é adiado para uma fase posterior, por ser o operador de menor demanda relatada | Proposta A, seção 12 | Etapa 5 |
| Porta JSON aceita também um único parâmetro de query string (JSON url-encoded) para clientes sem corpo em GET, ou fica restrita a POST | Proposta A, seção 12 | Etapa 4 |
| Formato do erro 400 do parser/limites — corpo estruturado com posição do erro vs. mensagem simples | Proposta A, seção 12 | Etapas 5 e 6 |
| Onde registrar os limites configuráveis (options pattern via DI, propriedade estática, parâmetro por chamada) | Proposta A, seção 12 | Etapa 6 |
| Nome da classe base `QueryableConfiguration<T>` — colide visualmente com o namespace raiz `Queryable` e com `System.Linq.Queryable` do BCL | Proposta B, seção 14 | Etapa 3 |
| `As()` aceita múltiplos aliases (`As("cpf", "documento")`) já na fase 2, ou fica para depois | Proposta B, seção 14 | Etapa 3 |
| **`cpf.value` some quando `cpf` é reconfigurado?** No modo permissivo, a entrada automática antiga permanece por padrão ao lado da nova — decidir se isso é aceitável (dois nomes públicos para o mesmo dado) ou se deveria ser removida automaticamente | Proposta B, seção 14 | Etapa 3 |
| `OnlyMapped()` deveria ser o padrão implícito para todo tipo que tem qualquer `QueryableConfiguration<T>`, em vez de opt-in explícito — tradeoff entre "mais seguro por padrão" e a garantia atual de "configurar um alias extra nunca restringe nada" | Proposta B, seção 14 | Etapa 3 |
| Onde mora a varredura de assembly (`AddQueryableConfigurationsFromAssembly`) — pacote núcleo ou pacote de extensões de DI separado | Proposta B, seção 14 | Etapa 3 |

**Risco específico a registrar: a Etapa 3 habilita uma mudança potencialmente *breaking* no
contrato HTTP do lado do consumidor.** Trocar `?cpf.value=` por `?cpf=` (o exemplo motivador da
proposta B, colapsando `Usuario.Cpf.Value`) não é breaking na biblioteca em si — é aditivo, o
alias antigo continua funcionando no modo permissivo (ver a decisão "`cpf.value` some?" acima).
Mas é breaking **para quem consome a API HTTP construída com a biblioteca**, se a mudança for
usada para *substituir* o alias antigo em vez de conviver com ele: qualquer cliente externo (SPA,
integração, Swagger salvo) que hoje monta `?cpf.value=...` para de funcionar no momento em que o
alias antigo for removido do contrato exposto. Recomendação a registrar explicitamente no
material de migração da Etapa 3 (e no `CLAUDE.md`/README quando a etapa fechar): tratar a adoção
de um novo alias configurado como **aditivo primeiro** — os dois aliases (automático e
configurado) convivem por um período de transição comunicado aos consumidores da API HTTP —, e só
remover o alias antigo (via `Ignore(...)` ou reconfiguração) depois desse período, nunca no mesmo
release em que o alias novo é introduzido.
