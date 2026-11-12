# Migração entre versões do Queryable.DynamicFilter

Mudanças de `10.1.0` até `10.12.0`, levantadas do código e das tags do repositório
(<https://github.com/SamuelGFDias/Queryable/tags>). `Queryable.DynamicFilter` e
`Queryable.DynamicFilter.EntityFrameworkCore` saem sempre juntos, com a mesma versão: atualize os
dois para o mesmo número.

Nenhuma versão desta faixa remove membro público nem quebra compilação de código que usava a API
documentada. O que muda é **comportamento**, em geral do lado de quem chama a API HTTP. Por isso,
para cada versão entre a de origem (exclusive) e a de destino (inclusive):

1. leia a seção;
2. rode as buscas sugeridas no projeto;
3. anote o que afeta o **contrato HTTP** para avisar a pessoa (frontend, integrações, Swagger
   salvo precisam saber).

Legenda: **[HTTP]** muda o que o cliente da API observa; **[código]** exige ou recomenda mudança
no código do projeto; **[aditivo]** recurso novo, opcional.

## Resumo rápido

| Versão | O que muda | Impacto |
| --- | --- | --- |
| 10.2.0 | Conversão de valores culture-invariant; guardas contra recursão; `in` mais estrito; README no pacote | **[HTTP]** decimais/datas, aliases de coleção somem |
| 10.3.0 | Mapa de caminhos com cache (`IPropertyPathProvider` singleton) | [aditivo] |
| 10.4.0 | Árvore de filtro composto (`FilterNode`) no motor | [aditivo] |
| 10.5.0 | Configuração fluente (`QueryableConfiguration<T>`) | [aditivo], recomenda revisar superfície |
| 10.6.0 | `Filter` (árvore JSON) em `QuerySpec<T>` e `RequestQuery` | [aditivo], cuidado com campo `filter` no corpo |
| 10.7.0 | Mini-linguagem `filter=` na query string; `RequestQuery.FilterExpression` | **[HTTP]** chave `filter` reservada |
| 10.8.0 | `FilterLimits` (profundidade, nós, tamanho, itens de `in`); 400 no binder | **[HTTP]** filtros compostos grandes rejeitados |
| 10.8.1 | `PagedQueryService` passa a respeitar limites do DI | **[HTTP]** se limites customizados |
| 10.9.0 | `MaxPageSize` (1000); limites configurados no conversor JSON | **[HTTP]** `pageSize > 1000` rejeitado |
| 10.10.0 | `FilterGroup` com igualdade por valor | [código] raro |
| 10.11.0 | Coleção em filtro/ordenação dá `ArgumentException` clara; `.Count` via fluente | [código] tratamento de erro |
| 10.12.0 | `FilterOnly()`/`SortOnly()`/`ForSort()`; enum numérico fora do enum rejeitado | **[HTTP]** enum inválido vira erro |

---

## 10.1.0 → 10.2.0

**Conversão de valores passou a usar `CultureInfo.InvariantCulture`** **[HTTP]**
Antes, `Convert.ChangeType`, `DateOnly.Parse` e `TimeOnly.Parse` usavam a cultura do servidor.
Num servidor pt-BR, `preco__gte=10.50` virava `1050` e `10,50` virava `10.5`. Agora vale sempre o
formato invariante: `.` é separador decimal e `,` é separador de milhar (`10,50` vira `1050`, sem
erro). Datas são lidas no formato invariante (`01/02/2025` é 2 de janeiro); use ISO
`yyyy-MM-dd`.

- Procure no frontend/integrações quem monta filtros numéricos com vírgula decimal ou datas em
  `dd/MM/yyyy`, e passe para `10.50` e `2025-02-01`.
- Servidores em cultura `en-US`/invariante não percebem diferença.

**Guardas na varredura de propriedades** **[HTTP]**
A varredura por reflexão não desce mais dentro de coleções, não reentra um tipo que já está no
caminho atual (navegação bidirecional) e para em 5 níveis. Consequências:

- Somem aliases que vinham de dentro de `List<T>`/coleções: `produtos.count`, `produtos.capacity`,
  `produtos.item...` e qualquer `x.colecao.campo`. A própria propriedade de coleção continua no
  mapa (e, a partir de 10.11.0, gera erro claro se usada). Para filtrar por quantidade, use
  `For(c => c.Produtos.Count).As("...")` (10.11.0+).
- Somem os aliases com mais de 5 níveis de navegação.
- Projetos com navegação bidirecional ou autorreferência que antes estouravam
  `StackOverflowException` passam a funcionar. Numa autorreferência (`Funcionario.Gestor` do tipo
  `Funcionario`), `gestor` entra no mapa mas `gestor.nome` não, porque o tipo já está no caminho.
  Para expor esse campo, use configuração fluente (10.5.0+): `For(f => f.Gestor.Nome).As("gestorNome")`.
- Busque no frontend chaves de filtro/`sort` com três ou mais pontos ou passando por coleção.

**Operador `in` usa a mesma conversão dos demais** **[HTTP]**
`Guid`, enum, `DateOnly`, `TimeOnly`, `Nullable<T>` e `null` passam a funcionar dentro de `in`.
Itens vazios são ignorados (`id__in=1,,2` = `id__in=1,2`) e uma lista sem nenhum item válido
(`id__in=,`) agora lança `ArgumentException` em vez de gerar filtro que não casa com nada.

**README empacotado nos dois pacotes** — só documentação.

## 10.2.0 → 10.3.0

**`IPropertyPathProvider` com cache** [aditivo]
`AddQueryableDynamicFilter()` passa a registrar `IPropertyPathProvider`
(`ReflectionPropertyPathProvider`) como **singleton**; `FilterBuilder` e `SortBuilder` recebem o
provider por construtor. O construtor sem parâmetros continua existindo.

- Se o projeto registrava `IFilterBuilder`/`ISortBuilder` à mão com `new FilterBuilder()`, prefira
  resolver pelo DI (a partir de 10.5.0 isso passa a ser necessário para a configuração fluente
  valer).
- Quem implementa `IPropertyPathProvider` próprio pode registrá-lo antes de
  `AddQueryableDynamicFilter()`, que usa `TryAdd`.

## 10.3.0 → 10.4.0

**Árvore `FilterNode`** [aditivo]
`FilterCondition`, `FilterGroup`, `FilterNot` (namespace `Queryable.Filtering`). O dicionário de
filtros passa a ser compilado pela árvore internamente, com resultado idêntico.
`IFilterBuilder` ganhou `BuildPredicate<T>(FilterNode)` como método padrão de interface:
implementações próprias de `IFilterBuilder` continuam compilando, mas lançam
`NotSupportedException` se receberem uma árvore. Se o projeto tem `IFilterBuilder` próprio e vai
usar filtros compostos, implemente a sobrecarga ou herde o comportamento de `FilterBuilder`.

## 10.4.0 → 10.5.0

**Configuração fluente** [aditivo]
`QueryableConfiguration<TEntity>` com `For(...).As(...)`, `Ignore(...)`, `OnlyMapped()`;
registro por `AddQueryableConfiguration<T>()` ou `AddQueryableConfigurationsFromAssembly(...)`.
Tudo continua igual sem configuração.

- Boa hora para **revisar a superfície exposta**: toda propriedade pública continua filtrável por
  padrão. Pergunte à pessoa se alguma entidade exposta tem campo sensível e proponha
  `OnlyMapped()` ou `Ignore(...)`. Restringir é mudança **[HTTP]**: combine com quem consome.
- `[Queryable("alias")]` continua funcionando; pode ser migrado para `For(...).As("alias")` se o
  domínio não deve referenciar o pacote.
- Ao **renomear** um alias, o alias automático antigo continua resolvendo em paralelo. Mantenha os
  dois por um período de transição e só depois remova o antigo com `Ignore(...)`.
- `new FilterBuilder()`/`new SortBuilder()` sem argumentos ignoram a configuração fluente.

## 10.5.0 → 10.6.0

**`Filter` (árvore JSON) em `QuerySpec<T>` e `RequestQuery`** [aditivo]
Corpo JSON com `{ "logic": "or", "children": [...] }`, `{ "field", "operator", "value" }` ou
`{ "not": ... }`. Combina por `AND` com os filtros do dicionário.

- Se o projeto tem classe própria que **herda** de `RequestQuery`/`QuerySpec<T>` e já declarava
  uma propriedade `Filter`, há conflito de nome; renomeie a do projeto.
- Corpos JSON que já mandavam um campo `filter` com outro formato agora são interpretados como
  `FilterNode` e podem falhar na desserialização.

## 10.6.0 → 10.7.0

**Mini-linguagem `filter=` na query string** **[HTTP]**
`?filter=(nome__contains=ana or nome__contains=joao) and ativo=true`; em `RequestQuery`, a
propriedade `FilterExpression`. Erro de sintaxe lança `FilterExpressionSyntaxException`.

- **A chave `filter` passou a ser reservada** no binder de `QuerySpec<T>`. Se alguma entidade tem
  propriedade chamada `Filter` e o cliente filtrava com `?filter=valor`, isso agora é lido como
  expressão e quase sempre dá erro de sintaxe. Exponha a propriedade com outro alias
  (`For(x => x.Filter).As("filtro")` ou `[Queryable("filtro")]`) e avise os clientes.
- A lógica do binder foi extraída para `QuerySpecModelBinder<T>.BuildSpec(query, limits)`, útil
  para Minimal API.
- `FilterOperators.All` passa a ser a lista oficial de operadores.

## 10.7.0 → 10.8.0

**Limites de segurança (`FilterLimits`)** **[HTTP]**
Filtros compostos (JSON ou `filter=`) passam a ter tetos: `MaxDepth = 6`, `MaxNodes = 100`,
`MaxExpressionLength = 4096`, `MaxInItems = 200`. Acima disso, `FilterLimitExceededException`.

- Se algum cliente monta filtros compostos grandes (lista `in` com centenas de ids, por exemplo),
  configure `AddQueryableDynamicFilter(limits => ...)` com tetos adequados. Os limites valem só
  para a árvore composta; filtros simples `campo__in=...` da query string não são contados em
  `MaxInItems`.
- No Caminho A (`[FromQuery] QuerySpec<T>`), erro de sintaxe e de limite passam a virar `400`
  pelo binder, em vez de `500`. Se o projeto tinha tratamento próprio para isso, pode simplificar.
- `RequestQueryExtensions.ToQuerySpec<T>(request, limits)` ganhou parâmetro opcional `limits`.
  Código que chama `ToQuerySpec` à mão deve passar o `FilterLimits` resolvido do DI. (O parâmetro
  novo é compatível em código-fonte; uma biblioteca intermediária compilada contra a versão
  anterior precisa ser recompilada.)

## 10.8.0 → 10.8.1

**`PagedQueryService` respeita os limites do DI** **[HTTP]**
Antes, `IPagedQueryService` validava sempre contra os defaults e ignorava
`AddQueryableDynamicFilter(Action<FilterLimits>)`. Agora usa os limites registrados.

- Projeto com limites **mais restritivos** passa a rejeitar requisições que antes passavam.
- Confira a ordem no `Program.cs`: `AddQueryableDynamicFilter(limits => ...)` tem que vir
  **antes** de `AddQueryableDynamicFilterEfCore()`. Na ordem inversa, os limites customizados
  continuam ignorados, agora sem nem o bug para explicar.

## 10.8.1 → 10.9.0

**`MaxPageSize` (default 1000)** **[HTTP]**
`pageSize` acima de `FilterLimits.MaxPageSize` lança `FilterLimitExceededException`
(`FilterLimitKind.MaxPageSize`), verificado no binder (vira `400`) e em `ToQuerySpec`/`PagedQueryService`.

- Busque clientes que pedem `pageSize` alto para "trazer tudo" (exportações, combos). Ou eles
  passam a paginar, ou o projeto sobe `MaxPageSize`.
- `Page`/`PageSize` `<= 0` continuam sendo ignorados em silêncio (viram 1 e 10). Isso é decisão
  de design, não bug.
- `QuerySpec<T>` montado à mão e aplicado direto por `IQuerySpecApplier` não passa por essa checagem.

**Limites configurados no conversor JSON** [aditivo]
`JsonSerializerOptions.AddQueryableFilterLimits(limits)`. Se o projeto configurou limites
**maiores** que os defaults e recebe filtro composto em corpo JSON, registre o conversor nas
`JsonOptions` (snippet em `setup.md`), senão o corpo continua barrado pelos defaults.

## 10.9.0 → 10.10.0

**`FilterGroup` compara por valor** [código]
Duas árvores com o mesmo conteúdo agora são `==` e têm o mesmo `GetHashCode()`, inclusive os
filhos de `FilterGroup`. Só afeta código que usava `FilterGroup` como chave de dicionário/cache ou
comparava árvores esperando igualdade por referência.

## 10.10.0 → 10.11.0

**Coleção em filtro ou ordenação dá erro claro** [código]
`?produtos=x` ou `?sort=produtos` lança `ArgumentException` com mensagem que aponta o caminho
(`.Count` via configuração fluente), em vez de `InvalidCastException` ou outro erro obscuro.

- Se o projeto tratava `InvalidCastException` para esse caso, troque por `ArgumentException`.
- Novo: `For(c => c.Produtos.Count).As("qtd_produtos")` permite filtrar e ordenar pela quantidade
  de itens (vira subconsulta `COUNT` no EF Core). `Any`/`all` sobre itens ainda não existe.

## 10.11.0 → 10.12.0

**Enum numérico fora do enum é rejeitado** **[HTTP]**
`situacao=999` (valor não definido no enum) antes rodava e devolvia lista vazia com `200`; agora
lança `ArgumentException`, igual a um nome inválido. Para enums `[Flags]`, combinações válidas
de bits continuam aceitas. Vale também dentro de `in`.

- Verifique se algum cliente manda valores numéricos de enum que não existem mais no código.

**Filtro e ordenação separados por alias** [aditivo]
`For(...).FilterOnly()`, `For(...).SortOnly()` e `ForSort(p => expressão).As("alias")`
(ordenação por expressão, nunca filtrável; `As(...)` obrigatório, senão
`InvalidOperationException` ao registrar).

- `For(...)` sem qualificador continua valendo para os dois usos: nada muda sem configuração nova.
- Um alias usado no lado errado (`FilterOnly` em `sort=`) recebe a mesma mensagem de campo
  inexistente.
- Quem implementa `IPropertyPathProvider` próprio: ganhou `GetPaths<T>(QueryableUsage)` e
  `GetSortExpressions<T>()` com implementação padrão; continua compilando, só não vai suportar
  `FilterOnly`/`SortOnly`/`ForSort` até sobrescrever.
- Quem chama `IPropertyPathProvider.GetPaths<T>()` direto: com configuração `FilterOnly`/`SortOnly`,
  esse mapa só contém o que vale nos dois usos.
- Útil para ordenação padrão composta: declare `ForSort(...).As("nomeExibicao")` e use
  `Sort = "nomeExibicao"` como default do `RequestQuery`.

---

## Depois de migrar

1. `dotnet build` sem erro nem aviso novo, testes passando.
2. Rode as requisições de verificação do `SKILL.md` (Passo 3).
3. Entregue à pessoa a lista das mudanças **[HTTP]** que se aplicam ao intervalo migrado, com o
   que precisa ser comunicado a quem consome a API.
4. Se o projeto ainda não trata erros de entrada como `400` fora do binder, proponha o tratamento
   descrito em `setup.md`, passo 5.
