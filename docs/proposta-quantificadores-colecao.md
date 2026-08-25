# Proposta: quantificadores `any`/`all` sobre propriedades de coleção no Queryable.DynamicFilter

> **STATUS: PROPOSTA PARA AVALIAÇÃO. NADA FOI IMPLEMENTADO.**
>
> Este documento não altera nenhum arquivo de código do repositório. Todos os blocos de código
> abaixo são **ilustrativos** da arquitetura proposta, não implementação real. O comportamento
> atual (propriedade de coleção registrada no mapa de aliases mas rejeitada como valor escalar)
> permanece **inalterado** enquanto esta proposta não for aprovada e implementada em fases
> separadas. Responde à issue #4 do GitHub ("Suporte a any/all em propriedades de coleção").

---

## 1. Resumo executivo

**Problema.** Hoje não existe forma de filtrar uma entidade pela condição de um ou mais itens de
uma coleção sua. `?produtos__ativo=true` não significa nada para o `FilterBuilder` — `produtos` é
um alias que aponta para uma propriedade de coleção, e usá-lo em uma condição escalar é rejeitado
com `ArgumentException` (seção 2). O único recurso indireto é mapear `Count` via configuração
fluent (`For(x => x.Produtos.Count).As("qtdProdutos")`), que resolve "quantos itens" mas não "que
condição os itens satisfazem".

**Proposta.** Um quinto tipo de nó na árvore de filtro (`FilterNode`), `FilterQuantifier`, que
representa "existe pelo menos um item de `Collection` que satisfaz `Inner`" (`Any`) ou "todos os
itens de `Collection` satisfazem `Inner`" (`All`). Compilado para `Enumerable.Any`/`Enumerable.All`
via `Expression.Call`, que o EF Core traduz para `EXISTS`/`NOT EXISTS` em SQL.

**Exemplos concretos:**

- `categorias any (produtos any (ativo=true))` — "categorias com algum produto ativo".
- `pedidos all (entregue=true)` — "todos os pedidos entregues" (também verdadeiro sem pedido — seção 6).
- `produtos any` (sem predicado) — "tem pelo menos um produto", sem condição artificial em `Inner`.

Como as quatro fases de `docs/proposta-filtros-compostos.md` (implementadas), esta proposta é
pensada em fases entregáveis independentemente e mantém tudo aditivo: nenhum código existente muda
de comportamento, e o erro atual continua sendo o que ocorre para quem não usa quantificadores.

---

## 2. Situação atual e limitações

Conferido em `src/Queryable/Extensions/PathExtension.cs` e
`src/Queryable/Builders/FilterBuilder.cs`:

- **A coleção entra no mapa de aliases, mas a recursão não desce nela.**
  `BuildPathsRecursively` registra a propriedade no dicionário (`result[fullAlias] = newPath`,
  `PathExtension.cs:71`) e faz `continue` quando `IsCollectionType(prop.PropertyType)` é
  verdadeiro (`PathExtension.cs:79`) — nenhuma propriedade *dentro* do item (`Categoria.Produtos`
  → `Produto.Nome`, `Produto.Ativo`) é alcançável por alias tipo `produtos.nome`. Deliberado
  (comentário em `PathExtension.cs:74-78`): evita recursão infinita em navegação bidirecional do
  EF Core e aliases lixo de `List<T>` (`.capacity`, `.count`).
- **Usar o alias de uma coleção em condição escalar já lança erro explícito.**
  `FilterBuilder.CompileCondition` (`FilterBuilder.cs:141`) resolve `Field` no mapa e, se
  `PathExtension.IsCollectionType(path[^1].PropertyType)` for verdadeiro, lança
  `ArgumentException` via `PathExtension.CollectionNotScalarMessage` (`PathExtension.cs:37-40`),
  que já cita o único recurso hoje disponível (`For(x => x.{Prop}.Count)`) e encerra com "Filtros
  sobre os itens da coleção (any/all) não são suportados." — a lacuna já está identificada no
  próprio código, não é leitura externa.
- **`Count` é o único recurso de coleção hoje, só via configuração fluent.**
  `PropertyPathExtractor.Extract` (`src/Queryable/Configuration/PropertyPathExtractor.cs:30-58`)
  aceita apenas cadeia de `MemberExpression` sobre `PropertyInfo` — `Count` é propriedade, então
  `For(c => c.Produtos.Count).As("qtdProdutos")` funciona hoje. Já `For(c =>
  c.Produtos.Any(p => p.Ativo))` lança `ArgumentException`
  (`PropertyPathExtractor.cs:60-61`), porque `Any(...)` é `MethodCallExpression`, rejeitada
  explicitamente. `Count` resolve "quantos", nunca "quais itens".
- **`IPropertyPathProvider.GetPaths<T>()` resolve sempre a partir do tipo raiz**
  (`src/Queryable/Interfaces/IPropertyPathProvider.cs:22`). Não há forma de pedir "o mapa de
  caminhos a partir do tipo `Produto`" isoladamente — necessário para os aliases *dentro* de um
  quantificador (seção 4).

Em suma: o modelo atual resolve campo escalar da entidade ou de navegação 1-para-1, mas não tem
caminho — nem no dicionário legado, nem na árvore `FilterNode` já implementada — para condição
sobre os *itens* de uma coleção 1-para-N.

---

## 3. Modelo na árvore

```csharp
public enum FilterQuantifierKind { Any, All }

public sealed record FilterQuantifier(
    string Collection,
    FilterQuantifierKind Kind,
    FilterNode? Inner
) : FilterNode;
```

`FilterQuantifier` é um quinto tipo de nó ao lado dos que `FilterNode.cs` já declara
(`FilterCondition`, `FilterGroup`, `FilterNot` — `src/Queryable/Filtering/FilterNode.cs:19-91`).
`Collection` é o alias da propriedade de coleção, resolvido no mapa da entidade *raiz*, como
qualquer `FilterCondition.Field` — mas em vez de rejeitar por ser coleção (seção 2), o compilador
passa a tratá-la como ponto de partida de um `Any`/`All`. `Inner` é opcional e resolvido contra um
mapa *diferente*: o do tipo do item da coleção (seção 4) — aí reside a complexidade nova.

### 3.1 `Inner` nulo — "não vazia"

`FilterQuantifier("produtos", Any, null)` significa "a coleção tem pelo menos um item", sem
condição sobre qual item. Justificativa de incluir esse caso no modelo:

- É o uso mais comum de `any` na prática (exemplo 3 da seção 1), recorrente em libs equivalentes
  (Django `exists()`, OData `$filter` sem lambda `any`/`all`).
- A alternativa — forçar `Inner` sempre presente e o cliente forjar uma condição sempre verdadeira
  (`id__gt=0`) — depende de um detalhe de implementação da entidade que a lib não garante, e é
  menos legível que a ausência explícita.
- `Inner == null` para `All` **não** é simétrico: "todos os itens (não há nenhum) satisfazem
  condição vazia" tende à trivialidade e não parece caso de uso real. Recomendação:
  `FilterQuantifier(_, All, null)` deve ser **rejeitado** na validação (mensagem explicando que
  `All` exige `Inner`) — assimetria deliberada entre os dois quantificadores.

### 3.2 Alternativas de modelo descartadas

- **(a) Operador `"any"`/`"all"` dentro de `FilterCondition`**, condição interna em `Value`
  (string) — reintroduz um parser dentro do valor, o problema que `FilterNode` existe para
  eliminar (seção 9 detalha).
- **(b) `FilterCollection(string Collection, bool RequireAll, FilterNode? Inner)`**, com `bool` em
  vez de `enum` — descartado por legibilidade (`FilterQuantifierKind.Any`/`.All` é autoexplicativo
  em código e mensagens de erro).
- **(c) Quantificador como `FilterGroup` especial** — descartado: `FilterGroup` combina *irmãos*
  no mesmo escopo (`ParameterExpression`); um quantificador introduz um `ParameterExpression`
  **novo**, contaminando o caso comum com uma preocupação só de coleção.

---

## 4. Resolução de caminhos relativa ao tipo do item

Ponto mais delicado da proposta: toca `IPropertyPathProvider`, projetado para resolver sempre a
partir do tipo raiz da consulta. `GetPaths<T>()` (`IPropertyPathProvider.cs:22`) é genérico sobre
`T`, sempre o tipo da consulta — `FilterBuilder.BuildPredicate<T>` (`FilterBuilder.cs:33,47,71`)
nunca troca esse `T`. Compilar `Inner` de `FilterQuantifier("produtos", Any, inner)` sobre
`Categoria` exige o mapa de `Produto` — um `Type` só conhecido em runtime (via
`path[^1].PropertyType`), não como argumento de tipo genérico literal; C# não permite chamar
`GetPaths<T>()` com um `Type` calculado em runtime como argumento de tipo.

### 4.1 Duas alternativas de DIM

**(a) Novo DIM não genérico**, ao lado do existente:

```csharp
public interface IPropertyPathProvider
{
    IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths<T>();

    IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths(Type type)
        => (IReadOnlyDictionary<string, List<PropertyInfo>>)
           typeof(IPropertyPathProvider)
               .GetMethod(nameof(GetPaths), Type.EmptyTypes)!
               .MakeGenericMethod(type)
               .Invoke(this, null)!;
}
```

A implementação padrão invoca a genérica por reflexão (`MakeGenericMethod` + `Invoke`) sobre o
mesmo `this`, passando pelo cache de `ReflectionPropertyPathProvider` (seção 4.2), que deveria
sobrescrever `GetPaths(Type)` com implementação direta (acesso interno ao próprio `_cache`),
evitando reflexão a cada quantificador compilado.

**(b) `FilterBuilder` chama `GetPaths<TItem>()` via reflexão diretamente**, sem tocar em
`IPropertyPathProvider`. Não muda a interface, mas empurra a reflexão para dentro do compilador, e
um `IPropertyPathProvider` externo sofre a mesma invocação sem chance de cache próprio.

**Recomendação:** (a) — segue o padrão que `IFilterBuilder`/`FilterBuilder.cs` já estabeleceram
nas fases de filtros compostos (aditivo via DIM, seção 9) e aproveita o cache já existente de
`ReflectionPropertyPathProvider` (`ReflectionPropertyPathProvider.cs:39`, indexado por `Type`).

### 4.2 Interação com `OnlyMapped`/`Ignore` do tipo do item

`ReflectionPropertyPathProvider.BuildFinalMap<T>` (`ReflectionPropertyPathProvider.cs:61-84`) já
aplica `OnlyMapped`/`Ignore` **por tipo**, via `QueryableConfigurationRegistry`. Como
`GetPaths(Type)` delega para a mesma máquina de cache/composição, funciona automaticamente para o
tipo do item: se existir `QueryableConfiguration<Produto>` com `OnlyMapped()`, `Inner` de um
quantificador sobre `Categoria.Produtos` respeita essa restrição — incluindo aliases que colapsam
value object do item (mesmo mecanismo de `docs/proposta-configuracao-fluent.md`, seção 6). Nenhuma
lógica nova de composição precisa ser escrita — é consequência direta de reusar o cache existente.

### 4.3 Aliases dentro do quantificador são relativos ao item

`produtos any (ativo=true and preco__gt=10)` resolve `ativo`/`preco` contra o mapa de `Produto` —
**não** `produtos.ativo`. Dois motivos: (1) simetria com `Count` (`For(c => c.Produtos.Count)` já
opera com `Produtos` como prefixo implícito, alias resultante `qtdProdutos`, não
`produtos.qtdProdutos`); (2) `PathExtension` nunca desce em coleção (seção 2), então
`produtos.ativo` nem existe no mapa da raiz — não há ambiguidade a resolver.

---

## 5. Sintaxe nas duas portas

### 5.1 Mini-linguagem (query string, parâmetro `filter`)

Extensão da gramática de `docs/proposta-filtros-compostos.md`, seção 4:

```ebnf
primary     := "(" expr ")" | quantifier | comparison
quantifier  := field ( "any" | "all" ) ( "(" expr ")" )?
```

`quantifier` entra como nova alternativa de `primary` (produção atual em
`FilterExpressionParser.cs:16-18`), ao lado de `"(" expr ")"` e `comparison`. `any`/`all` viram
novas palavras-chave nas mesmas condições de `and`/`or`/`not` hoje — case-insensitive, só como
token isolado fora de aspas.

| Expressão | Significado |
|---|---|
| `produtos any (ativo=true and preco__gt=10)` | pelo menos um produto ativo com preço > 10 |
| `produtos all (entregue=true)` | todos os produtos entregues (verdadeiro se vazia — seção 6) |
| `produtos any` | pelo menos um produto, qualquer que seja (`Inner = null`) |
| `not (produtos any (ativo=true))` | nenhum produto ativo |
| `categorias any (produtos any (ativo=true))` | quantificador aninhado (seção 6) |
| `produtos all` | **inválido** — `All` sem `Inner` é rejeitado (seção 3.1) |

O parêntese após `any`/`all` reaproveita a produção `"(" expr ")"` já existente — só a moldura
`field (any|all) (...)?` em volta é nova.

### 5.2 Porta JSON

Mesmo princípio de `FilterNodeJsonConverter.cs`: polimorfismo por presença de campo, sem `$type`.

```json
{ "collection": "produtos", "any": { "field": "ativo", "value": "true" } }
{ "collection": "produtos", "all": { "field": "entregue", "value": "true" } }
{ "collection": "produtos", "any": null }
```

Em `FilterNodeJsonConverter.ReadNode` (hoje testa `field` → `logic`+`children` → `not`, nessa
ordem, `FilterNodeJsonConverter.cs:90-112`): inserir checagem de `collection` antes de `field`:

```csharp
JsonElement? collectionElement = GetPropertyIgnoreCase(element, "collection");
if (collectionElement is not null)
    return ReadQuantifier(element, collectionElement.Value);
```

`ReadQuantifier` decide `Any`/`All` pela presença de `any` ou `all` (um dos dois, nunca os dois —
`JsonException` se ambos ou nenhum, espelhando o tratamento de `logic`/`children` ausentes em
`ReadGroup`, `FilterNodeJsonConverter.cs:145-149`). O valor pode ser `null` explícito (`Inner =
null`, só válido para `any`) ou um objeto de nó (recursão em `ReadNode`).

**Serialização (`Write`).** Simetricamente a `WriteNode` (`FilterNodeJsonConverter.cs:185-218`),
um novo `case FilterQuantifier quantifier:` escreve `{ "collection": ..., "any"/"all": <recursão
ou null> }` — round-trip completo, mesmo padrão dos três nós existentes.

---

## 6. Compilação

O compilador `FilterNode → Expression` (`FilterBuilder.Compile`, `FilterBuilder.cs:104-138`) ganha
`case FilterQuantifier quantifier:`. Esboço:

```csharp
private static Expression CompileQuantifier(
    FilterQuantifier quantifier, ParameterExpression parameter,
    IReadOnlyDictionary<string, List<PropertyInfo>> properties)
{
    if (!properties.TryGetValue(quantifier.Collection, out List<PropertyInfo>? path))
        throw new ArgumentException($"Campo '{quantifier.Collection}' não é pesquisável.");

    Type collectionType = path[^1].PropertyType;
    if (!PathExtension.IsCollectionType(collectionType))
        throw new ArgumentException($"Campo '{quantifier.Collection}' não é uma coleção.");

    Type itemType = collectionType.IsArray
        ? collectionType.GetElementType()!
        : collectionType.GetGenericArguments()[0];
    Expression collectionMember = path.Aggregate<PropertyInfo, Expression>(parameter, Expression.Property);

    ParameterExpression itemParameter = Expression.Parameter(itemType, "item");
    var itemProperties = pathProvider.GetPaths(itemType); // seção 4

    Expression predicate = quantifier.Inner is null
        ? Expression.Constant(true)   // Any sem predicado: só checa não-vazia
        : Compile(quantifier.Inner, itemParameter, itemProperties);

    LambdaExpression lambda = Expression.Lambda(predicate, itemParameter);
    string methodName = quantifier.Kind == FilterQuantifierKind.Any ? "Any" : "All";
    MethodInfo method = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
        .First(m => m.Name == methodName && m.GetParameters().Length == 2)
        .MakeGenericMethod(itemType);

    return Expression.Call(method, collectionMember, lambda);
}
```

Notas: a busca por reflexão do overload de dois parâmetros reaproveita a técnica que
`BuildInExpression` já usa para `Contains` — nenhum padrão novo no arquivo. **Aninhamento**:
`Compile(quantifier.Inner, itemParameter, itemProperties)` é a mesma função recursiva que já
processa `FilterGroup`/`FilterNot`/`FilterCondition` — um `FilterQuantifier` dentro de `Inner` cai
no mesmo `case`, com `itemParameter`/`itemProperties` do nível externo virando parâmetro/mapa do
nível interno; nenhuma pilha explícita é necessária (diferente da guarda de `FilterLimitValidator`,
seção 7, que é sobre entrada não confiável, não sobre compilar árvore já validada). **`All` sobre
coleção vazia é verdadeiro** (vacuous truth de `Enumerable.All`/`∀`, preservada pela tradução SQL —
`NOT EXISTS` sem linhas é sempre verdadeiro): documentar no XML doc e no README, é a armadilha mais
provável — `pedidos all (entregue=true)` inclui clientes **sem nenhum pedido**; padrão correto para
"tem pedido e todos entregues" é `pedidos any and pedidos all (entregue=true)`.

---

## 7. Limites de segurança

`FilterLimitValidator.Validate` (`src/Queryable/Filtering/FilterLimitValidator.cs:36-87`) percorre
a árvore iterativamente com `Stack<(FilterNode Node, int Depth)>` no heap (motivação em
`FilterLimitValidator.cs:12-19`: evitar que a própria validação estoure a pilha de chamadas na
árvore que ela existe para rejeitar) — se aplica sem alteração a uma árvore com
`FilterQuantifier`.

**Mudanças propostas no validador:**

1. `FilterQuantifier` conta como nó normal para `MaxNodes` (`FilterLimitValidator.cs:52-58`) — o
   `switch` (linhas 68-85) ganha `case FilterQuantifier quantifier:` que empilha `quantifier.Inner`
   (quando não nulo) com `depth + 1`, mesmo padrão de `FilterNot` (linhas 79-81), e soma **+1 de
   profundidade** ao aninhamento contado por `FilterLimits.MaxDepth`.
2. **Novo limite: `FilterLimits.MaxQuantifierDepth`, default sugerido `2`.** Distinto de
   `MaxDepth`: mede quantidade de `FilterQuantifier` *aninhados entre si*, não o aninhamento geral
   de `AND`/`OR`/`NOT`. Cada quantificador compila para `EXISTS`/`NOT EXISTS` correlacionado por
   subconsulta — dois níveis é razoável; três ou mais tendem a produzir planos caros ou degradar
   para scan aninhado por linha, algo que `MaxDepth = 6` (default de filtros compostos) não protege
   especificamente. Verificado com a mesma forma de erro (`FilterLimitExceededException`, novo
   `FilterLimitKind.MaxQuantifierDepth`).
3. **`MaxNodes`** já limita indiretamente quantos quantificadores uma árvore pode ter —
   complementa, não substitui, o teto dedicado de profundidade.

Pontos de validação: os mesmos quatro já existentes para filtros compostos
(`FilterExpressionParser.Parse`, `FilterNodeJsonConverter.Read`,
`QuerySpecModelBinder<T>.BuildSpec`, `RequestQueryExtensions.ToQuerySpec<T>()`) — nenhum ponto
novo, só extensão do que já é validado.

---

## 8. Tradução para SQL

- **`Any`** → `EXISTS (SELECT 1 FROM <tabela-filha> WHERE <fk> = <chave do pai> AND <predicado>)`;
  sem `Inner`, o `WHERE` interno é só a junção.
- **`All`** → `NOT EXISTS (... WHERE <fk> = <chave do pai> AND NOT (<predicado>))` — "para todo
  item, condição vale" como "não existe item para o qual não vale". Confirma a vacuous truth da
  seção 6.

**Testes obrigatórios na suíte EF (SQLite in-memory).** `List<T>.AsQueryable()` executa qualquer
`Expression` que o C# aceite em memória, incluindo `Enumerable.Any`/`.All`, mesmo quando não
traduzível por um provider real — um teste que só cobre `tests/Queryable.Tests` pode passar com
árvore que, contra um `DbContext` real, lança `InvalidOperationException` ("could not be
translated"), área historicamente sensível a mudança de provider EF Core (split query vs.
subconsulta correlacionada). **Atenção ao plano de execução:** `EXISTS`/`NOT EXISTS` correlacionado
é subconsulta por linha do pai — quantificadores aninhados multiplicam o custo por nível; testes
devem confirmar, no caso aninhado, que a query permanece `EXISTS`/`NOT EXISTS` (não produto
cartesiano seguido de filtro em memória, tradução degradada silenciosa que o EF Core pode produzir
sem exceção).

---

## 9. Compatibilidade

| Item | Muda? | Detalhe |
|---|---|---|
| `FilterNode` | não | `FilterQuantifier` é um quinto tipo irmão de `FilterCondition`/`FilterGroup`/`FilterNot` |
| `IFilterBuilder.BuildPredicate<T>(FilterNode)` | não (assinatura) | compila árvores com `FilterQuantifier`; sem quantificador, comportamento idêntico |
| `IPropertyPathProvider` | aditivo | ganha `GetPaths(Type)` como DIM (seção 4.1) |
| `PathExtension.CollectionNotScalarMessage`/`IsCollectionType` | não | reaproveitados por `CompileQuantifier` para validar que `Collection` é coleção |
| `FilterLimits`/`FilterLimitKind` | aditivo | ganham `MaxQuantifierDepth` (seção 7), default sugerido `2` |
| `FilterNodeJsonConverter` | aditivo (interno) | `ReadNode`/`WriteNode` reconhecem `collection`+(`any`\|`all`) |
| `FilterExpressionParser`/`Tokenizer` | aditivo (interno) | gramática ganha `quantifier`; `any`/`all` viram palavras-chave |
| `QueryableConfiguration<TEntity>`/`PropertyPathExtractor` | não | fora do escopo — `For(...)` continua rejeitando `.Any(...)`/`.All(...)` como seletor |
| Pacote `Queryable.EntityFrameworkCore` | não | continua consumindo `Expression<Func<T,bool>>`; árvore/quantificador internos ao pacote base |

### 9.1 `any`/`all` como construção sintática própria, não como operador de `FilterOperators`

`FilterOperators.All` (`src/Queryable/Filtering/FilterOperators.cs:23-26`) é a fonte única dos
operadores de **condição escalar** (`eq`, `neq`, `gt`, `lt`, `gte`, `lte`, `contains`, `in`), todos
na forma `campo__operador=valor` com valor string única (ou CSV para `in`). `any`/`all` não cabem
por dois motivos: (1) o "valor" de um quantificador é uma sub-árvore `FilterNode` inteira
(`Inner`), não uma string — encaixar em `FilterCondition.Value` reintroduziria um parser dentro do
próprio valor, a complexidade que `FilterNode` existe para eliminar do transporte (ver
`docs/proposta-filtros-compostos.md`, seção 3.1); (2) `FilterOperators.All` é consumido assumindo
operador escalar por `FilterBuilder.ParseKey` e pela produção `comparison := field ( "__" operator
)? "=" value` — um quantificador não é comparação, é construção de escopo (introduz
`ParameterExpression` novo, seção 6).

**Recomendação:** `any`/`all` como construção sintática própria (produção `quantifier`, seção 5.1;
`FilterQuantifier`, seção 3), não item de `FilterOperators.All` — coerente com o dicionário legado,
que não ganha suporte a quantificador; só mini-linguagem e JSON, portas que já representam árvore.

---

## 10. Plano de implementação em fases

Cada fase é entregável e reversível independentemente, seguindo o padrão das fases já concluídas
de `docs/proposta-filtros-compostos.md`. A última tag publicada é `v10.9.0`; sugestão de versão
inicial para a fase 1: **`v10.13.0`** (minor, aditivo, coerente com o versionamento por etapa do
plano de implementação).

1. **Fase 1 — árvore + compilador + limites.** `FilterQuantifier`/`FilterQuantifierKind` em
   `Queryable.Filtering`; `IPropertyPathProvider.GetPaths(Type)` (seção 4.1); `CompileQuantifier`
   em `FilterBuilder` (seção 6); `FilterLimits.MaxQuantifierDepth` e `case FilterQuantifier` em
   `FilterLimitValidator` (seção 7). Testável com árvores montadas manualmente, sem parser nem
   JSON. Ganho direto para o cliente: zero — mas a base fica pronta e testada.
2. **Fase 2 — porta JSON + mini-linguagem.** `FilterNodeJsonConverter` ganha
   `collection`+(`any`\|`all`) (seção 5.2); `FilterExpressionParser`/`Tokenizer` ganham a produção
   `quantifier` e as palavras-chave (seção 5.1). Sem mudança em `QuerySpec<T>`, `RequestQuery` ou
   model binders — `FilterQuantifier` é só mais um tipo de nó nos mesmos campos.
3. **Fase 3 — documentação e testes SQL.** README com a semântica de coleção vazia para `All`
   (seção 6), exemplos nas duas portas, suíte completa de tradução em
   `tests/Queryable.EntityFrameworkCore.Tests` contra SQLite in-memory (seção 8), incluindo caso
   aninhado e verificação de plano de execução.

---

## 11. Estratégia de testes

Mesma separação em camadas usada nas fases já implementadas de filtros compostos:

1. **Modelo e limites.** `FilterQuantifier` montado manualmente: profundidade excedendo
   `MaxQuantifierDepth`; `FilterQuantifier(_, All, null)` rejeitado; nó contando para `MaxNodes`;
   combinação com `MaxDepth` geral.
2. **Resolução de caminhos do item.** `GetPaths(Type)` devolvendo o mesmo mapa que
   `GetPaths<TItem>()` (paridade genérico/não genérico); interação com `OnlyMapped()`/`Ignore(...)`
   do tipo do item; cache por `Type` vale também via `GetPaths(Type)`.
3. **Compilador.** Contra lista em memória: `Any` com predicado verdadeiro/falso por item; `Any`
   sem `Inner` (vazia vs. não vazia); `All` com todos satisfazendo, nenhum satisfazendo, coleção
   vazia (verdadeiro — vacuous truth); quantificador aninhado de dois níveis.
4. **Sintaxe — mini-linguagem.** Parser aceita `campo any (...)`, `campo all (...)`, `campo any`
   sem parênteses; rejeita `campo all` sem `Inner` (`FilterExpressionSyntaxException`);
   case-insensitive; combinação com `and`/`or`/`not` ao redor.
5. **Sintaxe — JSON (`FilterNodeJsonTests.cs`).** Round-trip de `FilterQuantifier` (com e sem
   `Inner`); erro quando `any`+`all` juntos ou nenhum dos dois com `collection`; `Inner: null`
   aceito só para `any`.
6. **Tradução real (SQLite in-memory).** `Any`/`All` simples retornando conjunto correto; `All`
   sobre coleção vazia retornando verdadeiro no banco real; quantificador aninhado sem "could not
   be translated"; teste que excede `MaxQuantifierDepth` confirmando `FilterLimitExceededException`
   lançada **antes** de qualquer tradução (a árvore nunca chega ao provider do EF Core).

---

## 12. Questões em aberto

1. **`MaxQuantifierDepth = 2` é o número certo?** Default conservador sem medição de custo real em
   produção — validar com plano de execução antes de fixar, análogo à recomendação sobre `OR` em
   `docs/proposta-filtros-compostos.md`, seção 8.
2. **`FilterQuantifier(_, All, null)` deveria ser aceito como "coleção vazia" em vez de
   rejeitado?** Seção 3.1 recomenda rejeitar por falta de caso de uso real — confirmar com
   consumidores antes de fechar a API.
3. **`Collection` aceita caminho aninhado (`pedido.itens any (...)`) ou só alias direto?** O
   modelo não impõe restrição, mas o caso não foi testado explicitamente nesta proposta.
4. **Nome do DIM — `GetPaths(Type)` (overload) ou nome distinto (`GetPathsForType(Type)`)**, para
   reduzir ambiguidade de overload resolution em reflexão?
5. **Cache único ou separado entre `GetPaths<T>()` e `GetPaths(Type)`?** Hoje só existe
   `_cache: ConcurrentDictionary<Type, ...>` (`ReflectionPropertyPathProvider.cs:39`) — evitar duas
   entradas para o mesmo tipo precisa ser verificado na implementação.
6. **`GetElementType()`/`GetGenericArguments()[0]` cobrem todo formato de coleção suportado por
   `IsCollectionType`?** Vale confirmar que nenhuma implementação customizada de `IEnumerable<T>`
   escapa dessa extração de `itemType`.
7. **Texto exato do erro 400 para `FilterLimitKind.MaxQuantifierDepth`** — mesma decisão que
   `docs/proposta-filtros-compostos.md`, seção 12, já deixou em aberto para os limites originais.
