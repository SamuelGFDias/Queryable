# Proposta: filtros compostos (OR, agrupamento e NOT) no Queryable.DynamicFilter

> **STATUS: PROPOSTA PARA AVALIAÇÃO. NADA FOI IMPLEMENTADO.**
>
> Este documento não altera nenhum arquivo de código do repositório. Todos os blocos de código
> abaixo são **ilustrativos** da arquitetura proposta, não implementação real. O comportamento
> atual da biblioteca (`Filters` como `Dictionary<string,string>`, combinação exclusiva por
> `AND`) permanece **inalterado** enquanto esta proposta não for aprovada e implementada em
> fases separadas.

---

## 1. Resumo executivo

**Problema.** Hoje o `FilterBuilder` só sabe combinar condições de filtro com `AND`
(`Expression.AndAlso`, sempre). Não há forma de expressar "campo A = X **ou** campo B = Y",
não há agrupamento com parênteses, não há negação (`NOT`), e — efeito colateral da chave
`campo__operador` ser única no dicionário — não é possível aplicar o mesmo operador duas vezes
sobre o mesmo campo (ex.: `nome__contains=a` **e** `nome__contains=b` simultaneamente), porque a
segunda chave sobrescreve a primeira.

**Proposta.** Introduzir uma árvore de filtro (`FilterNode`) como representação intermediária
única entre "o que o cliente pediu" e "a `Expression` compilada". O caminho atual baseado em
`Dictionary<string,string>` passa a ser um **adaptador** que produz essa árvore (sempre como um
grupo `AND` de condições) — não um segundo motor de compilação. Duas novas portas de entrada
alimentam a mesma árvore: uma mini-linguagem textual (para query string / GET) e um formato JSON
(para corpo de requisição / montagem programática no frontend).

**Impacto.** Aditivo em toda a superfície pública. `Filters` continua existindo e funcionando
exatamente como hoje. Os novos recursos (`Filter`, mini-linguagem, JSON) são opt-in: se não
forem usados, o comportamento observável é idêntico ao atual, porque literalmente passam pelo
mesmo compilador via o mesmo adaptador. `IFilterBuilder` ganha a capacidade nova por *default
interface method*, sem quebrar implementações externas existentes.

---

## 2. Situação atual e limitações

Conferido em `src/Queryable/Builders/FilterBuilder.cs`:

- `IFilterBuilder.BuildPredicate<T>(IDictionary<string, string> queryParams)` é o único ponto de
  entrada de filtragem (`src/Queryable/Interfaces/IFilterBuilder.cs`).
- A implementação percorre `queryParams` e combina **toda** condição com `Expression.AndAlso`
  (linhas 48-50) — não existe caminho para `OR`, agrupamento ou negação.
- Operadores suportados (`SupportedOperators`, linha 10): `eq` (padrão, quando a chave não tem
  sufixo `__operador`), `neq`, `gt`, `lt`, `gte`, `lte`, `contains` (só para `string`), `in`
  (lista CSV separada por vírgula, ver `BuildInExpression`).
- A chave de cada filtro é `campo__operador` (`ParseKey`, linha 59), resolvida contra os aliases
  construídos por `PathExtension.BuildPropertyPaths<T>()` (suporta navegação aninhada via `.`,
  com guarda de profundidade `MaxDepth = 5`, guarda de ciclo e guarda de coleção —
  `src/Queryable/Extensions/PathExtension.cs`).
- `QuerySpec<T>.Filters` (`src/Queryable/Core/QuerySpec.cs`, linha 7) é um
  `Dictionary<string, string>` — **uma chave por par `campo__operador`**. Isso implica um limite
  estrutural: **não é possível representar duas condições com o mesmo campo e o mesmo operador**
  (ex.: `nome__contains=a` **e** `nome__contains=b`), pois a segunda sobrescreve a primeira no
  dicionário antes mesmo de chegar ao `FilterBuilder`. Esse não é um bug do `FilterBuilder` — é
  uma limitação do formato de transporte (`Dictionary`), e reaparece como argumento a favor da
  árvore proposta na seção 3.
- `RequestQuery.QueryFilter` (`src/Queryable/Core/RequestQuery.cs`) é uma string única no formato
  `campo__operador=valor`, com pares separados por `;` se a string contiver `;`, senão por `,`
  (comentário no próprio código explica que `;` deve ser usado quando algum valor é uma lista CSV
  de `in`, para não colidir com o separador de pares). Essa heurística de separador já é, na
  prática, uma tentativa de contornar a ambiguidade da vírgula — a mini-linguagem proposta na
  seção 4 resolve isso de forma definitiva com listas entre parênteses.
- `QuerySpecModelBinder<T>` (`src/Queryable/Extensions/QuerySpecModelBinder.cs`) lê a query
  string do ASP.NET Core e popula `QuerySpec<T>.Filters` diretamente a partir dos parâmetros,
  reconhecendo `page`, `pageSize`, `sort`, `skipTotalCount` e o padrão `Filters[chave]` usado pelo
  Swagger UI (`SwaggerFilterRegex`); qualquer outra chave vira uma entrada em `Filters`.

Em suma: o modelo atual é suficiente para "todas as condições combinadas com E", mas não
consegue expressar disjunção, agrupamento, negação, nem duplicidade de campo+operador.

---

## 3. Arquitetura proposta

### 3.1 A árvore como representação única

```csharp
public abstract record FilterNode;

public sealed record FilterCondition(string Field, string Operator, string Value) : FilterNode;

public enum FilterLogic { And, Or }

public sealed record FilterGroup(FilterLogic Logic, IReadOnlyList<FilterNode> Children) : FilterNode;

public sealed record FilterNot(FilterNode Inner) : FilterNode;
```

`FilterCondition` reaproveita o mesmo par `(campo, operador, valor)` que o `FilterBuilder` já
sabe interpretar hoje (`ParseKey` + `ConvertValue`/`ConvertScalar`/`BuildInExpression`) — nenhuma
dessas rotinas de conversão de tipo precisa mudar, elas só passam a ser chamadas a partir de um
nó de árvore em vez de uma entrada de dicionário.

**O ponto central desta proposta:** o `FilterBuilder` compilado passa a conhecer **um único
formato de entrada**, a árvore `FilterNode`. O caminho atual baseado em
`Dictionary<string, string>` não é reescrito para produzir `Expression` por conta própria — ele
vira um **adaptador burro** que traduz o dicionário em:

```csharp
new FilterGroup(FilterLogic.And, dictionary.Select(kv => (FilterNode)ToCondition(kv)).ToList());
```

e entrega essa árvore ao mesmo compilador que atende a mini-linguagem e o JSON. Isso é
deliberado: a compatibilidade com o comportamento atual deixa de ser uma promessa mantida por
disciplina de código (duas implementações que "devem" produzir o mesmo resultado, e podem
divergir com o tempo, code review desatento, ou um fix aplicado só de um lado) e passa a ser uma
**consequência estrutural** — não existem dois motores de compilação para divergir, porque existe
apenas um: o compilador de `FilterNode`. Qualquer alteração futura no comportamento de `eq`,
`contains`, `in` etc. é feita em um único lugar e vale automaticamente para os três caminhos de
entrada (dicionário, mini-linguagem, JSON).

### 3.2 Fluxo proposto

```
                         ┌─────────────────────┐
  Dictionary<string,str> │  Adaptador (legado)  │
  (QuerySpec.Filters) ──►│  Dict → FilterGroup  │───┐
                         └─────────────────────┘   │
                                                     │
  string (query string)  ┌─────────────────────┐   │      ┌──────────────────┐      ┌────────────┐
  "a=1 and (b=2 or c=3)" │  Parser mini-        │   ├─────►│  Compilador de    │─────►│ Expression │
                    ─────►│  linguagem → árvore  │───┤      │  FilterNode →     │      │ <Func<T,   │
                         └─────────────────────┘   │      │  Expression       │      │  bool>>    │
                                                     │      └──────────────────┘      └────────────┘
  JSON (corpo POST)      ┌─────────────────────┐   │
  { "logic": "or", ... } │  System.Text.Json    │───┘
                    ─────►│  → FilterNode        │
                         └─────────────────────┘
```

O compilador (`FilterNode → Expression`) é uma função recursiva simples:

```csharp
Expression Compile(FilterNode node, ParameterExpression parameter, /* mapa de paths */)
    => node switch
    {
        FilterCondition c => CompileCondition(c, parameter),          // reaproveita ParseKey/ConvertValue/BuildInExpression atuais
        FilterNot n        => Expression.Not(Compile(n.Inner, parameter)),
        FilterGroup { Logic: FilterLogic.And } g =>
            g.Children.Select(ch => Compile(ch, parameter)).Aggregate(Expression.AndAlso),
        FilterGroup { Logic: FilterLogic.Or } g =>
            g.Children.Select(ch => Compile(ch, parameter)).Aggregate(Expression.OrElse),
        _ => throw new NotSupportedException()
    };
```

Quando a árvore de origem é o adaptador do dicionário legado (`FilterGroup(And, [...])` "achatado
de um nível"), este compilador produz **exatamente** a mesma sequência de `Expression.AndAlso`
que o código atual gera hoje, porque `Aggregate(Expression.AndAlso)` sobre uma lista é
equivalente ao `foreach` + `finalExpr = Expression.AndAlso(finalExpr, condition)` existente.
Dicionário vazio deve continuar mapeando para `x => true` (o `FilterBuilder` atual já faz isso
quando `finalExpr == null`, linha 53-55) — o adaptador precisa preservar esse caso especial (grupo
`AND` vazio ⇒ predicado sempre verdadeiro).

---

## 4. Gramática da mini-linguagem

Porta 1 — para uso em GET / query string, onde uma árvore JSON seria pouco natural.

```ebnf
expr       := orExpr
orExpr     := andExpr ( "or" andExpr )*
andExpr    := unary ( "and" unary )*
unary      := "not"? primary
primary    := "(" expr ")" | comparison
comparison := field ( "__" operator )? "=" value
```

**Precedência:** `not` liga mais forte que `and`, que liga mais forte que `or`; parênteses
sobrepõem qualquer precedência. Ou seja, `a or b and c` equivale a `a or (b and c)`, e
`not a and b` equivale a `(not a) and b`.

**Palavras-chave.** `and`, `or`, `not` são reconhecidas *case-insensitive* (`AND`, `And`, `and`
são equivalentes) e só são tratadas como palavra-chave quando aparecem como token isolado fora de
uma string entre aspas — nunca como substring de um valor não citado.

**Aspas e escape — regra precisa.** O valor de uma `comparison` **deve** ser delimitado por
aspas duplas (`"..."`) sempre que contiver qualquer um destes: espaço, `(`, `)`, `,`, `=`, ou
texto que colida literalmente com `and`/`or`/`not` como token isolado. Dentro de um valor entre
aspas, uma aspas dupla literal é escapada como `\"`; uma barra invertida literal é escapada como
`\\`. Fora de aspas, o valor termina no primeiro caractere de espaço, `)`, `,` ou fim da string —
não há escape fora de aspas (se o valor precisa de qualquer caractere especial, ele **precisa**
de aspas, sem exceção). Um valor entre aspas que não é fechado antes do fim da expressão é erro
de sintaxe, não é "absorvido" até o fim da string.

**Operador `in`.** A lista de valores é delimitada por parênteses e separada por vírgula:
`id__in=(1,2,3)`. Cada item individual segue a mesma regra de aspas acima (item com vírgula,
espaço etc. precisa de aspas: `tag__in=("a, b",c)`). Isso substitui de vez a ambiguidade que hoje
existe em `RequestQuery.QueryFilter`, onde a vírgula é ao mesmo tempo separador de pares e
separador de itens de `in`, obrigando o chamador a trocar para `;` manualmente.

**Exemplos.**

| Expressão | Válida? | Observação |
|---|---|---|
| `nome=joao` | sim | equivalente a `nome__eq=joao` hoje |
| `nome__contains=jo and ativo=true` | sim | equivalente ao `Filters` atual com duas chaves |
| `(nome__contains=ana or nome__contains=joao) and ativo=true` | sim | não representável hoje |
| `not ativo=false` | sim | negação de uma condição simples |
| `id__in=(1,2,3)` | sim | lista sem ambiguidade de separador |
| `tag__in=("a, b",c)` | sim | item de `in` com vírgula interna, entre aspas |
| `nome="and joão"` | sim | valor que colide com palavra-chave, entre aspas |
| `nome=and joão` | **não** | `and` fora de aspas é interpretado como operador lógico, quebra o parse |
| `nome=joão silva` | **não** | espaço fora de aspas termina o valor antes do esperado |
| `(nome=ana` | **não** | parêntese não fechado |
| `nome="joão` | **não** | aspas não fechadas |
| `id__in=1,2,3` | **não** | lista de `in` fora de parênteses (ambígua com separador de campos) |

---

## 5. Porta JSON

Porta 2 — para corpo de requisição (POST) ou montagem programática no frontend, onde uma árvore
já é a estrutura natural e não há necessidade de gramática nem parser.

```json
{
  "logic": "or",
  "children": [
    { "field": "nome", "operator": "contains", "value": "ana" },
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

Regras de desserialização propostas:

- `operator` ausente em um nó de condição ⇒ assume `"eq"` (mesmo padrão do dicionário atual).
- Um nó é reconhecido como `FilterCondition` quando tem `field`; como `FilterGroup` quando tem
  `logic` + `children`; como `FilterNot` quando tem `not` (objeto único). `System.Text.Json` com
  um `JsonConverter<FilterNode>` customizado resolve o polimorfismo por presença de campo — sem
  precisar de um discriminador de tipo (`$type`) explícito, mantendo o JSON legível para quem o
  monta manualmente no frontend.
- Uma condição folha (`{ "field": ..., "value": ... }`, sem `logic`) no nível raiz do body é
  aceita como atalho e tratada como `FilterGroup(And, [essa condição])` — não obriga o chamador a
  embrulhar um filtro simples em um grupo.

Como o JSON já é uma árvore, não há parser à escrever para esta porta — é desserialização
direta com o suporte nativo de `System.Text.Json` mais o `JsonConverter` de polimorfismo.

---

## 6. Compatibilidade

| Item | Muda? | Detalhe |
|---|---|---|
| `QuerySpec<T>.Filters` (`Dictionary<string,string>`) | não | continua existindo, mesmo tipo, mesmo comportamento |
| `FilterBuilder.BuildPredicate<T>(IDictionary<string,string>)` | não (assinatura e resultado) | passa a delegar internamente para o adaptador + compilador de árvore, mas o `Expression` resultante é idêntico para a mesma entrada |
| `IFilterBuilder` | aditivo | ganha `BuildPredicate<T>(FilterNode)` como **default interface method** (ver 6.1) |
| `RequestQuery.QueryFilter` | não | continua aceitando o formato atual (`;`/`,`) |
| `RequestQuery` | aditivo | ganha propriedade nova (nome a definir — seção 12) para a expressão da mini-linguagem |
| `QuerySpec<T>` | aditivo | ganha `FilterNode? Filter { get; set; }`, padrão `null` |
| `QuerySpecModelBinder<T>` | aditivo | passa a reconhecer um parâmetro novo (ex.: `filter=`) além dos já existentes; se ausente, nenhum comportamento muda |
| `ISortBuilder`, `IQuerySpecApplier`, paginação | não | fora do escopo desta proposta |
| Pacote `Queryable.EntityFrameworkCore` | não | consome `Expression<Func<T,bool>>` como hoje; a árvore é interna ao pacote base |

**Regra de combinação quando `Filters` e `Filter` são usados juntos:** se `Filter` (árvore) for
`null`, o comportamento é idêntico ao atual — só `Filters` é considerado. Se ambos estiverem
preenchidos, o predicado final é `AND(adaptador(Filters), Filter)` — os dois conjuntos de
condições se combinam por `AND`, nunca um sobrescreve o outro. Isso preserva o caso mais comum
(código legado que só usa `Filters` continua funcionando) e dá um caminho de migração incremental
(adicionar uma condição composta sem reescrever os filtros simples existentes).

### 6.1 Por que `default interface method` na `IFilterBuilder`

```csharp
public interface IFilterBuilder
{
    Expression<Func<T, bool>> BuildPredicate<T>(IDictionary<string, string> queryParams);

    // Novo — default interface method (C# 8+)
    Expression<Func<T, bool>> BuildPredicate<T>(FilterNode filter)
        => throw new NotSupportedException(
               "Implementação de IFilterBuilder não suporta árvore de filtro composta.");
}
```

Qualquer consumidor externo que já tenha uma classe própria implementando `IFilterBuilder` (por
exemplo, um projeto que customizou a lógica de filtro) continua compilando sem alteração — o novo
método tem corpo padrão e não é obrigatório de implementar. Sem o `default`, adicionar um método
à interface seria um **breaking change** em C# (toda implementação existente deixaria de
compilar). Com o `default`, a mudança é estritamente aditiva: quem não sobrescreve, recebe o
comportamento padrão (que pode ser tanto uma exceção informativa quanto — na implementação
concreta `FilterBuilder` fornecida pela própria lib — a compilação real da árvore).

---

## 7. Limites de segurança

Aceitar uma expressão booleana arbitrária vinda de um cliente externo (query string ou JSON de
body) é, por natureza, aceitar um programa pequeno para ser executado pelo servidor. Sem limites,
um cliente malicioso ou só descuidado pode enviar uma árvore profundamente aninhada ou com um
número enorme de nós, forçando o `FilterBuilder` a montar uma `Expression` gigantesca e o banco a
executar um `WHERE` patológico — um vetor de negação de serviço tanto na camada de aplicação
(tempo de montagem da árvore de expressão, uso de memória) quanto no banco (parsing/planejamento
de uma cláusula `WHERE` desproporcional).

Limites propostos, todos configuráveis (ex.: via opções registradas no DI, com os valores abaixo
como default):

| Limite | Default proposto | Motivo |
|---|---|---|
| Profundidade máxima de aninhamento de grupos/`not` | 6 | acompanha a mesma ordem de grandeza do `MaxDepth = 5` já usado em `PathExtension` para navegação de propriedades |
| Número total de nós na árvore (`FilterCondition` + `FilterGroup` + `FilterNot`, somados recursivamente) | 100 | teto generoso para filtros legítimos, baixo o suficiente para impedir árvores de milhares de nós |
| Tamanho máximo da string de entrada (mini-linguagem) | 4096 caracteres | evita gastar tempo de parsing em payloads absurdamente longos antes mesmo de contar nós |
| Quantidade máxima de itens em uma lista `in` | 200 | mesma motivação do limite de nós, aplicada à explosão horizontal de uma única condição |

A validação deve ocorrer **antes** da compilação para `Expression` (idealmente logo após o parse
ou a desserialização), retornando um erro de requisição inválida (400) claro quando um limite é
excedido — nunca deixar o limite ser descoberto só quando o banco já recebeu a query.

---

## 8. Impacto em performance

`OR` muda o formato do plano de execução gerado pelo banco de forma que merece atenção antes de
liberar em endpoints de alto volume:

- Um `OR` que atravessa **navegações diferentes** (ex.: `pedido.cliente.nome=X or
  pedido.endereco.cidade=Y`) tende a impedir o otimizador de usar um índice composto único e
  frequentemente força um plano com `UNION`/scan mais amplo do que a soma das duas condições
  filtradas separadamente.
- De forma geral, `OR` costuma inviabilizar o uso de um índice que o `AND` equivalente
  aproveitaria sem problema — é um padrão conhecido em otimização de consultas relacionais, não
  específico deste projeto.
- Grupos com muitos filhos (`OR` de 10+ condições) tendem a degradar ainda mais o plano,
  reforçando a importância dos limites da seção 7 também como controle de performance, não só de
  segurança.

**Recomendação:** antes de habilitar a mini-linguagem/JSON em um endpoint de alto volume de
tráfego, medir o plano de execução gerado para os padrões de filtro esperados (com dados em
volume próximo ao de produção), e considerar liberar `OR`/agrupamento primeiro em endpoints de
menor criticidade ou de uso interno.

---

## 9. Plano de implementação em fases

Cada fase é entregável e reversível de forma independente — nenhuma depende de a seguinte já
estar em produção.

1. **Fase 1 — árvore + adaptador + compilador.** Introduzir `FilterNode` e as três records
   (`FilterCondition`, `FilterGroup`, `FilterNot`), o adaptador `Dictionary → FilterGroup(And)`,
   e o compilador `FilterNode → Expression`. `FilterBuilder.BuildPredicate<T>(IDictionary)` passa
   a delegar para esse caminho internamente. Ganho direto para o cliente da lib: zero (a API
   pública não muda ainda) — mas a base estrutural fica pronta e testável isoladamente (compilador
   testado com árvores montadas manualmente em código, sem depender de nenhum parser).

2. **Fase 2 — porta JSON.** `QuerySpec<T>.Filter`, desserialização via `System.Text.Json` +
   `JsonConverter<FilterNode>` polimórfico. Fase barata porque não exige parser algum — só mapear
   JSON para os records já existentes desde a fase 1. Habilita OR/agrupamento/NOT para clientes
   que montam o filtro programaticamente (frontend, integrações) via POST.

3. **Fase 3 — parser da mini-linguagem.** Implementação da gramática da seção 4 (tokenizer +
   parser recursivo descendente), exposta via a nova propriedade de `RequestQuery` e reconhecida
   pelo `QuerySpecModelBinder<T>` para GET/query string.

4. **Fase 4 — limites e observabilidade.** Aplicação dos limites da seção 7 (configuráveis),
   validação antes da compilação, e métricas/logs (ex.: contagem de nós, profundidade, tempo de
   compilação) para acompanhar uso real antes/depois de liberar em endpoints de maior volume.

---

## 10. Estratégia de testes

O repositório está ganhando agora uma suíte xUnit (`tests/Queryable.Tests` e
`tests/Queryable.EntityFrameworkCore.Tests`, esta última executando contra SQLite in-memory). A
arquitetura em árvore separa naturalmente os testes propostos em três camadas independentes, cada
uma testável sem depender das outras:

1. **Parser (string → árvore).** Testes puramente sintáticos: dada uma expressão da mini-
   linguagem, a árvore `FilterNode` resultante é a esperada (ou o parser rejeita com erro claro,
   para os casos inválidos da tabela da seção 4). Não precisa de `IQueryable`, banco, nem tipo de
   entidade real — só a gramática.

2. **Compilador (árvore → `Expression`).** Dada uma árvore `FilterNode` montada manualmente em
   código, o `Expression<Func<T,bool>>` resultante produz o resultado correto quando aplicado a
   uma lista de objetos em memória. Cobre a lógica de `AND`/`OR`/`NOT` e a reutilização correta de
   `ParseKey`/`ConvertValue`/`BuildInExpression` por trás de cada `FilterCondition`.

3. **Tradução (Expression → SQL, via SQLite).** Testes que rodam o predicado composto contra um
   `DbContext` real apontando para SQLite in-memory (o padrão já usado em
   `tests/Queryable.EntityFrameworkCore.Tests`), verificando que o EF Core efetivamente **traduz**
   a árvore composta para SQL executável — inclusive os casos com `OR` atravessando navegação e
   `NOT`. Esta camada importa especificamente porque **`List<T>.AsQueryable()` executa qualquer
   `Expression` que o LINQ-to-Objects aceite**, mascarando erros de tradução que só aparecem
   quando o provider do EF Core (`IQueryProvider` real) tenta converter a árvore de expressão em
   SQL — um teste que só usa a camada 2 (compilador) pode passar com uma expressão que o EF Core
   não consegue traduzir em produção.

---

## 11. Alternativas consideradas e descartadas

- **(a) Estender a convenção de chave achatada com índices (`or[0].campo=`, `or[1].campo=`
  etc.).** Mantém o formato `Dictionary<string,string>` sem introduzir um tipo novo, mas não
  aninha bem: representar `(a or b) and (c or (d and not e))` nesse esquema exige uma notação de
  índices aninhados que rapidamente fica ilegível tanto para escrever quanto para revisar em
  código de chamada, e reintroduz na prática a necessidade de um parser — só que um parser pior,
  porque a "gramática" está implícita na convenção de nomes de chave em vez de explícita.

- **(b) Adotar OData / `$filter`.** É um padrão estabelecido e poderoso (suporta praticamente
  qualquer operação relacional), mas é pesado para o escopo desta lib: exige aderir a uma
  especificação inteira, tem superfície de recursos muito maior do que o necessário aqui
  (funções, `$expand`, `$select`, tipos complexos), e traria uma dependência (ou uma
  reimplementação parcial) desproporcional ao problema de "adicionar OR/agrupamento/NOT" que esta
  proposta resolve com uma gramática pequena e auditável.

- **(c) Aceitar expressão LINQ dinâmica em string (ex.: System.Linq.Dynamic /
  System.Linq.Dynamic.Core).** Permite escrever qualquer predicado C#-like diretamente, mas abre
  superfície de injeção e execução arbitrária: uma string vinda de cliente externo sendo
  interpretada como código LINQ dinâmico é, na prática, aceitar lógica arbitrária definida pelo
  chamador, com risco de acesso a membros/métodos não previstos e de negação de serviço via
  expressões custosas — incompatível com o modelo de segurança que a lib mantém hoje (só
  propriedades mapeadas por `PathExtension` são endereçáveis, com guardas de profundidade e
  ciclo).

---

## 12. Questões em aberto

Decisões que dependem de alinhamento com o time antes da fase 3 (ou mesmo da fase 2) avançar:

- **Nome do parâmetro/propriedade novos.** Sugestão de trabalho: `filter` na query string e
  `Filter` em `RequestQuery`/`QuerySpec<T>`, mas precisa confirmar que não colide com uso
  existente em consumidores da lib.
- **`NOT` já na fase 1 ou adiado?** A proposta inclui `FilterNot` desde o modelo de árvore
  inicial (fase 1), mas cabe decidir se o suporte a `not` na mini-linguagem (fase 3) entra junto
  ou é adiado para uma fase 3.5, já que é o operador de menor demanda relatada até agora.
- **Porta JSON via GET com valor url-encoded?** Definir se a árvore JSON pode ser aceita também
  como um único parâmetro de query string (JSON serializado e url-encoded), para clientes que não
  conseguem enviar corpo em GET, ou se a porta JSON fica restrita a POST.
- **Mensagens de erro do parser/limites.** Formato do erro 400 retornado quando a mini-linguagem
  é inválida ou um limite da seção 7 é excedido (corpo estruturado com posição do erro na string,
  vs. mensagem simples) — impacta a experiência de quem consome a API publicamente.
- **Onde registrar os limites configuráveis da seção 7** (options pattern via DI, propriedade
  estática, parâmetro por chamada) — decisão de design a alinhar com o padrão já usado em outras
  configurações da lib.
