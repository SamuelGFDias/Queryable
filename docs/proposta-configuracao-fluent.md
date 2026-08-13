# Proposta: configuração fluent de mapeamento no Queryable.DynamicFilter

> **STATUS: PROPOSTA PARA AVALIAÇÃO. NADA FOI IMPLEMENTADO.**
>
> Este documento não altera nenhum arquivo de código do repositório. Todos os blocos de código
> abaixo são **ilustrativos** da arquitetura proposta, não implementação real. O atributo
> `[Queryable]` (`src/Queryable/Attributes/QueryableAttribute.cs`) continua existindo e
> funcionando exatamente como hoje, em qualquer cenário — esta proposta é **aditiva**, nunca
> substitutiva.

---

## 1. Resumo executivo

**Problema.** O único jeito de renomear ou restringir um campo endereçável hoje é anotar a
propriedade no domínio com `[Queryable(alias)]`. Isso falha para um caso concreto e recorrente:
colapsar um value object (ex.: `Usuario.Cpf.Value`) em um alias plano (`cpf`) sem vazar o nome
interno da propriedade do VO no contrato HTTP. Anotar a propriedade aninhada não funciona, porque
o alias aninhado é sempre `prefixo + "." + alias` (seção 3) — o resultado seria `cpf.cpf`, não
`cpf`. E anotar o próprio tipo `Cpf` afeta toda entidade que o usa, porque é um tipo compartilhado.
Além disso, como não existe cache em `PathExtension.BuildPropertyPaths<T>()`, cada requisição de
filtro ou de ordenação refaz do zero a varredura recursiva por reflexão do grafo de propriedades
do tipo — e as duas operações (`FilterBuilder` e `SortBuilder`) refazem essa varredura de forma
independente na mesma requisição quando ambas são usadas.

**Proposta.** Uma API fluent de configuração por tipo (`QueryableConfiguration<TEntity>`), que
declara aliases e ignora propriedades fora do domínio, resolvida em runtime por um novo
`IPropertyPathProvider` com cache por tipo. O atributo `[Queryable]` continua sendo o caminho
"zero configuração" — a configuração fluent é uma camada opcional por cima, para os casos que o
atributo não cobre bem (value objects, campos sensíveis, mapeamento centralizado fora do domínio).

**Impacto.** Aditivo em toda a superfície pública. Nenhum tipo sem configuração fluent muda de
comportamento. `FilterBuilder` e `SortBuilder` ganham construtores adicionais que aceitam
`IPropertyPathProvider`, mas os construtores sem parâmetro existentes continuam funcionando,
usando um provider padrão só de reflexão. Como efeito colateral desejado, a introdução do cache
(fase 1, isolada da configuração fluent) já resolve a duplicidade de varredura entre
`FilterBuilder` e `SortBuilder`, com ganho de performance sem exigir nenhuma mudança de API do
consumidor.

---

## 2. O pedido

O pedido veio do analista Wallace, resumido a partir da mensagem original:

- Não quer **remover** `[Queryable]` — "pode ter aplicação que a gente use". A proposta deve ser
  aditiva, mantendo o atributo funcionando.
- Quer poder renomear/expor campos **sem anotação no domínio** — ou seja, sem precisar decorar a
  entidade ou o value object com atributos só para satisfazer a API de filtro/ordenação.
- Exemplo concreto dado por ele: `Usuario.Cpf.Value` deveria virar `Usuario.Cpf` no contrato HTTP
  (`?cpf=...`), em vez de `Usuario.Cpf.Value` (`?cpf.value=...`).
- Sugestão dele: "acho que dá pra criar um fluent api, igual a lib de auditoria" — referência ao
  `AuditConfigurator<T>` do pacote AuditLog, que a equipe já usa e conhece o formato de escrita.

---

## 3. Por que o atributo não resolve

O comportamento atual está inteiro em `src/Queryable/Extensions/PathExtension.cs`,
`BuildPathsRecursively` (linhas 32-51):

```csharp
string alias = attr?.Alias?.ToLowerInvariant() ?? prop.Name;
string fullAlias = string.IsNullOrEmpty(prefix)
                       ? alias
                       : $"{prefix}.{alias}";
```

O alias de uma propriedade aninhada é sempre a concatenação `prefixo + "." + alias-local` — nunca
um alias plano independente da posição na árvore. Isso tem duas consequências diretas para o caso
`Usuario.Cpf.Value`:

1. **Anotar a propriedade folha não funciona.** Se `Cpf.Value` for anotado com
   `[Queryable("cpf")]`, o prefixo (`cpf`, vindo da propriedade de navegação `Usuario.Cpf`, que já
   teria virado o segmento `cpf` do caminho por ser o nome da propriedade) é concatenado com o
   alias local (`cpf`), produzindo `cpf.cpf` — não `cpf`. Não existe, no modelo atual, uma forma de
   um alias "substituir" o prefixo em vez de se juntar a ele.
2. **Anotar o tipo do value object afeta todo mundo.** `Cpf` é um tipo compartilhado — qualquer
   entidade que o use (não só `Usuario`) herdaria o mesmo alias para a propriedade `Value`, porque
   o atributo é declarado uma única vez na definição da propriedade `Value` dentro da classe `Cpf`,
   não por ponto de uso.

**Efeito observável hoje:** a API pública expõe `?cpf.value=123` para filtrar por CPF, vazando o
detalhe de implementação (o value object `Cpf` e seu campo interno `Value`) diretamente no
contrato HTTP — exatamente o tipo de acoplamento que o pedido do Wallace quer eliminar.

Vale registrar também, como pano de fundo do "sem anotação no domínio": a checagem que faria o
atributo ser obrigatório está comentada no próprio código —

```csharp
// if (attr == null)
//     continue;
```

(`PathExtension.cs`, linhas 35-36) — por isso, hoje, **toda** propriedade pública de `T` e de suas
navegações (respeitando as guardas de coleção, ciclo e `MaxDepth = 5`) já é endereçável por
filtro/ordenação, com ou sem `[Queryable]`. O atributo hoje só serve para *renomear* um alias que,
por padrão, já existiria com o nome da propriedade — nunca para restringir o que é exposto.

---

## 4. Ressalva sobre "igual à lib de auditoria"

A sugestão do Wallace ("acho que dá pra criar um fluent api, igual a lib de auditoria") está certa
sobre a **forma de escrita**, mas precisa de uma ressalva sobre o **mecanismo por trás**, para
alinhar expectativa antes de qualquer implementação.

Conferido em `/home/samuel.dias/dev/audit_log/src/AuditLog.Abstractions/AuditConfigurator.cs`:

```csharp
protected AuditPropertyBuilder<TEntity, TProperty> For<TProperty>(
    Expression<Func<TEntity, TProperty>> expression)
{
    return default!;
}

protected void ForOwned<TOwned>(
    Expression<Func<TEntity, TOwned>> expression,
    Action<AuditOwnedBuilder<TEntity, TOwned>> configure)
    where TOwned : class?
{
}
```

Os métodos de `AuditConfigurator<TEntity>` têm corpo vazio ou retornam `default!`. Essa classe
**nunca executa em runtime** — ela é um DSL consumido por um source generator Roslyn em tempo de
compilação, que lê a árvore de `Expression` estaticamente (via análise de sintaxe do código-fonte,
não via execução) e gera código C# equivalente. É por isso que o corpo pode estar vazio: o valor
de retorno nunca é observado, porque o método nunca roda de verdade.

No `Queryable.DynamicFilter`, isso **não é necessário**. `PathExtension.BuildPropertyPaths<T>()`
já constrói o mapa de caminhos por reflexão **em runtime**, uma vez por tipo. Uma configuração
fluent aqui pode — e deve — ser uma classe comum, instanciada e executada normalmente no início da
aplicação, com os métodos (`For`, `Ignore`, `OnlyMapped`) tendo corpo real que popula uma estrutura
de dados em memória. Não há necessidade de source generator, de análise estática de sintaxe, nem
de builders com retorno `default!`.

**Resumo da ressalva:** mesmo formato de escrita para quem consome a API (uma classe que herda de
um tipo base e chama `For(...)` no construtor), mecanismo de resolução completamente diferente e
bem mais simples — reflexão em runtime sobre uma `Expression` de verdade, não geração de código em
tempo de compilação.

---

## 5. API proposta

```csharp
public abstract class QueryableConfiguration<TEntity> where TEntity : class
{
    protected QueryablePropertyBuilder<TEntity> For<TProperty>(
        Expression<Func<TEntity, TProperty>> selector);

    protected void Ignore<TProperty>(Expression<Func<TEntity, TProperty>> selector);

    protected void OnlyMapped();   // ativa modo opt-in para este tipo
}

public sealed class QueryablePropertyBuilder<TEntity>
{
    public QueryablePropertyBuilder<TEntity> As(string alias);
}
```

Exemplo de uso completo:

```csharp
public sealed class UsuarioQueryConfiguration : QueryableConfiguration<Usuario>
{
    public UsuarioQueryConfiguration()
    {
        OnlyMapped();
        For(u => u.Nome).As("nome");
        For(u => u.Cpf.Value).As("cpf");        // colapsa o value object
        For(u => u.Perfil.Descricao).As("perfil");
        Ignore(u => u.SenhaHash);
    }
}
```

Neste exemplo, `?cpf=12345678900` passa a resolver diretamente para `Usuario.Cpf.Value`, sem
qualquer anotação em `Usuario` nem em `Cpf`, e sem afetar outras entidades que também usem `Cpf`
(porque a configuração é declarada por `TEntity`, não por propriedade do VO).

---

## 6. Mecanismo

`For` recebe `Expression<Func<TEntity, TProperty>>`. Para `u => u.Cpf.Value`, o corpo dessa
expressão é uma cadeia de `MemberExpression`: o nó mais externo é `Value` (`MemberExpression` cujo
`Expression` interno é outro `MemberExpression`, `Cpf`), cujo `Expression` interno, por sua vez, é
o `ParameterExpression` (`u`).

O algoritmo é: partir do `Body` da lambda, desembrulhar recursivamente enquanto for
`MemberExpression` (empilhando o `PropertyInfo` de cada nó), até alcançar o `ParameterExpression`;
inverter a pilha para obter a ordem raiz→folha. O resultado é **exatamente** o mesmo
`List<PropertyInfo>` que `PathExtension` já usa hoje como valor de cada entrada do dicionário —
o mesmo tipo que `FilterBuilder.BuildPredicate` e `SortBuilder.ApplySort` já sabem consumir via
`path.Aggregate<PropertyInfo, Expression>(parameter, Expression.Property)`
(`src/Queryable/Builders/FilterBuilder.cs`, linha 29-31; `src/Queryable/Builders/SortBuilder.cs`,
linha 30-32). Não é um formato novo — é um segundo produtor para a mesma estrutura de dados que já
existe, o que evita qualquer duplicação de lógica de tradução para `Expression` no `FilterBuilder`
e no `SortBuilder`.

Detalhes que a implementação precisa cobrir:

- **Desembrulhar `UnaryExpression`.** Uma conversão implícita (ex.: boxing de um `TProperty` de
  tipo valor, ou um `Convert` inserido pelo compilador) aparece como `UnaryExpression` envolvendo o
  `MemberExpression`. É preciso desembrulhar (`((UnaryExpression)node).Operand`) antes de checar se
  o nó é `MemberExpression`, senão expressões como `u => (object)u.Nome` ou seletores de propriedade
  de tipo `Enum`/`struct` falhariam a validação sem necessidade.
- **Rejeitar qualquer coisa que não seja cadeia de propriedades.** Chamada de método
  (`u => u.Nome.ToUpper()`), indexador (`u => u.Enderecos[0]`) ou campo (`FieldInfo`, não
  `PropertyInfo`) devem lançar uma exceção clara na configuração (falha rápida, na inicialização da
  aplicação, não em tempo de requisição), citando a expressão recebida em texto
  (`selector.ToString()`) para facilitar o diagnóstico — ex.:
  `"A expressão 'u => u.Nome.ToUpper()' não é uma cadeia de propriedades válida para For()."`

---

## 7. Resolução, provider e cache

Hoje `PathExtension.BuildPropertyPaths<T>()` é `public static` e chamado diretamente por
`FilterBuilder.BuildPredicate<T>` (linha 19) e por `SortBuilder.ApplySort<T>` (linha 20) — cada um
com sua própria chamada, sem nenhum cache em nenhum dos dois lugares nem em `PathExtension`. Uma
requisição que filtra e ordena no mesmo request (padrão comum: `?nome__contains=jo&sort=-nome`)
provoca **duas varreduras recursivas completas por reflexão** do mesmo tipo `T`, uma para cada
builder.

Proposta: introduzir uma abstração de resolução de caminhos —

```csharp
public interface IPropertyPathProvider
{
    IReadOnlyDictionary<string, List<PropertyInfo>> GetPaths<T>();
}
```

com uma implementação padrão que:

1. Constrói o mapa por reflexão pura chamando `PathExtension.BuildPropertyPaths<T>()` (que
   **continua `public static`**, inalterado — é o caminho só-reflexão usado internamente pelo
   provider padrão).
2. Aplica por cima a configuração fluent registrada para `T`, se existir (regras de mesclagem na
   seção 8).
3. Guarda o resultado final em `ConcurrentDictionary<Type, IReadOnlyDictionary<string,
   List<PropertyInfo>>>`, indexado por `typeof(T)`.

`FilterBuilder` e `SortBuilder` passam a receber `IPropertyPathProvider` por construtor.
**Compatibilidade obrigatória:** ambos mantêm também um construtor sem parâmetros, que instancia
internamente o provider padrão só-reflexão (sem configuração fluent, sem DI) — hoje há código,
inclusive nos testes do repositório (`tests/Queryable.Tests/FilterBuilderTests.cs`,
`tests/Queryable.Tests/SortBuilderTests.cs`), que instancia `new FilterBuilder()` /
`new SortBuilder()` diretamente, fora do container de DI. Quebrar esse construtor seria um
breaking change não relacionado ao objetivo desta proposta.

**Por que o cache não precisa de invalidação.** A configuração fluent (via `QueryableConfiguration<T>`)
é registrada uma única vez, na inicialização da aplicação, e não muda depois — não há API proposta
para reconfigurar um tipo em runtime. Isso significa que o mapa de caminhos resolvido para cada
`Type` é estável durante toda a vida do processo: uma vez calculado, pode ficar em cache
indefinidamente, sem necessidade de expiração, de invalidação por evento, nem de qualquer
mecanismo de sincronização além da concorrência de escrita inicial que o próprio
`ConcurrentDictionary` já resolve (`GetOrAdd`).

O ganho de performance da fase 1 (seção 11) é independente da configuração fluent: só introduzir
`IPropertyPathProvider` com cache, mantendo tudo o resto igual, já elimina a segunda varredura
redundante quando filtro e ordenação são usados juntos na mesma requisição.

---

## 8. Semântica de mesclagem e modo opt-in

Ao configurar `For(u => u.Cpf.Value).As("cpf")`, o alias `cpf` colide com a entrada automática que
`PathExtension` já geraria para a própria propriedade de navegação `Usuario.Cpf` (alias `cpf` →
caminho `[Cpf]`, apontando para o objeto `Cpf` inteiro, não para `Cpf.Value`). As regras propostas:

| Modo | Comportamento |
|---|---|
| **Permissivo (padrão, sem `OnlyMapped()`)** | Entradas configuradas **sobrescrevem** entradas automáticas de mesmo alias. As demais entradas automáticas do tipo permanecem intactas. No exemplo, `cpf` passa a resolver para `Cpf.Value`; a entrada automática `cpf.value` (apontando para o mesmo `Cpf.Value` pelo caminho antigo) continua existindo em paralelo, a menos que a decisão em aberto (seção 14) opte por removê-la. |
| **Opt-in (`OnlyMapped()`)** | **Somente** as entradas declaradas via `For(...)` existem para aquele tipo — toda entrada automática por reflexão é descartada. `Ignore(...)` se torna redundante nesse modo (o que não está declarado já não existe), mas continua útil no modo permissivo, onde é a única forma de remover uma entrada automática sem reconfigurar todo o tipo com `OnlyMapped()`. |

`OnlyMapped()` é uma decisão **por tipo** — chamado dentro do construtor de
`QueryableConfiguration<Usuario>`, afeta só a resolução de `Usuario`. Qualquer outro tipo sem
configuração fluent (ou com configuração fluent mas sem chamar `OnlyMapped()`) continua
inteiramente permissivo, exatamente como hoje. É essa regra que torna a mudança estritamente
aditiva: nenhum consumidor existente perde acesso a um campo que já usava, a menos que
explicitamente opte por restringir um tipo específico.

`OnlyMapped()` é também, deliberadamente, a resposta operacional para a checagem comentada em
`PathExtension.cs` (`if (attr == null) continue;`, linhas 35-36): em vez de reativar aquela
checagem globalmente — o que tornaria `[Queryable]` obrigatório e quebraria todo consumidor
existente que hoje depende do comportamento "tudo endereçável por padrão" — o opt-in fica
disponível tipo a tipo, sob controle explícito de quem configura. Isso fecha, por exemplo, a
exposição inadvertida de `SenhaHash` (campo sensível, público na classe `Usuario`, hoje filtrável e
ordenável só porque é uma propriedade pública) sem exigir nenhuma mudança em consumidores que usam
outros tipos ou que ainda não configuraram `Usuario`.

---

## 9. Compatibilidade

| Item | Muda? | Detalhe |
|---|---|---|
| `[Queryable]` (atributo) | não | continua funcionando exatamente como hoje, em qualquer tipo, com ou sem configuração fluent |
| `PathExtension.BuildPropertyPaths<T>()` | não (assinatura/comportamento) | continua `public static`, passa a ser usado internamente pelo provider padrão como o caminho só-reflexão |
| `FilterBuilder` | aditivo | ganha construtor `FilterBuilder(IPropertyPathProvider)`; construtor sem parâmetros é mantido, usando provider padrão só-reflexão |
| `SortBuilder` | aditivo | mesma mudança que `FilterBuilder` |
| `IFilterBuilder`, `ISortBuilder` | não | assinaturas de método inalteradas |
| `AddQueryableDynamicFilter()` | aditivo | continua registrando os builders; passa a também registrar `IPropertyPathProvider` (`TryAddSingleton`, coerente com o cache não precisar de escopo por request) |
| Tipos sem `QueryableConfiguration<T>` | não | resolução 100% por reflexão, igual a hoje, incluindo a propriedade endereçável por padrão sem anotação |
| Tipos com `QueryableConfiguration<T>`, modo permissivo | aditivo | ganham aliases extras/sobrescritos; nada existente deixa de funcionar |
| Tipos com `QueryableConfiguration<T>` + `OnlyMapped()` | restritivo, mas opt-in | só quem chama `OnlyMapped()` naquele tipo sofre a restrição; decisão explícita do time |
| Performance (varredura por requisição) | melhora | cache por tipo elimina a segunda varredura redundante entre `FilterBuilder` e `SortBuilder` (ver seção 7), mesmo sem usar configuração fluent |

---

## 10. Interação com a proposta de filtros compostos

A proposta de filtros compostos (`docs/proposta-filtros-compostos.md`) introduz uma árvore
`FilterNode` (`FilterCondition`, `FilterGroup`, `FilterNot`) como representação intermediária entre
"o que o cliente pediu" e a `Expression` compilada. O nó folha, `FilterCondition(string Field,
string Operator, string Value)`, precisa resolver `Field` contra o mesmo mapa de caminhos que
`FilterBuilder.BuildPredicate` já usa hoje via `properties.TryGetValue(propKey, out ...)`
(`FilterBuilder.cs`, linha 25).

As duas propostas são **ortogonais** — uma não depende da outra para ser implementada — mas se
tocam exatamente nesse ponto: esta proposta (configuração fluent + `IPropertyPathProvider`) é a
**camada de baixo** da proposta de filtros compostos. O compilador `FilterNode → Expression`
descrito na proposta de filtros compostos (seção 3.1 daquele documento) resolve `Field` da mesma
forma que `FilterBuilder` resolve `propKey` hoje: consultando o mapa de aliases do tipo. Se essa
proposta (configuração fluent) já estiver implementada quando o compilador de árvore for escrito,
o compilador simplesmente injeta `IPropertyPathProvider` e ganha automaticamente os aliases
configurados (incluindo o colapso de value objects) para qualquer condição da árvore, sem nenhum
código adicional. Se a ordem for invertida — filtros compostos primeiro — o compilador nasce
consumindo `PathExtension.BuildPropertyPaths<T>()` diretamente (como `FilterBuilder` faz hoje) e
troca a dependência para `IPropertyPathProvider` quando esta proposta for implementada, sem
mudança de contrato público em nenhuma das duas.

**Não há conflito de ordem de implementação.** As fases desta proposta (seção 11) e as fases da
proposta de filtros compostos podem ser executadas em qualquer intercalação; o único ponto de
integração é a troca de "consultar `PathExtension` estático" por "consultar
`IPropertyPathProvider` injetado" dentro do compilador de árvore, que é uma mudança interna e
localizada, não uma mudança de contrato.

---

## 11. Plano de implementação em fases

Cada fase é entregável e reversível de forma independente.

1. **Fase 1 — `IPropertyPathProvider` + cache, sem configuração.** Introduzir a interface, a
   implementação padrão (só reflexão, delegando a `PathExtension.BuildPropertyPaths<T>()`) e o
   cache por tipo (`ConcurrentDictionary<Type, ...>`). `FilterBuilder` e `SortBuilder` passam a
   aceitar o provider por construtor, mantendo o construtor sem parâmetros. Zero mudança de API
   observável para quem já usa a lib — ganho puro de performance (elimina a segunda varredura
   redundante descrita na seção 7).

2. **Fase 2 — configuração fluent, extração de caminho e registro no DI (modo permissivo).**
   `QueryableConfiguration<TEntity>`, `QueryablePropertyBuilder<TEntity>`, o algoritmo de extração
   de `Expression` para `List<PropertyInfo>` (seção 6), e a mesclagem permissiva (configurado
   sobrescreve automático, seção 8). Registro via
   `AddQueryableConfigurationsFromAssembly(Assembly)` e `AddQueryableConfiguration<TConfiguration>()`.

3. **Fase 3 — `OnlyMapped()` e `Ignore(...)`.** Modo opt-in por tipo, com a regra de mesclagem
   restritiva da seção 8.

4. **Fase 4 — documentação e guia de migração.** Exemplos de uso (incluindo o caso de value
   object do Wallace), guia de quando usar `[Queryable]` vs. configuração fluent, e recomendação de
   quando adotar `OnlyMapped()` para tipos com campos sensíveis.

---

## 12. Estratégia de testes

O repositório tem suíte xUnit em `tests/Queryable.Tests` (inclui hoje `PathExtensionTests.cs`,
`FilterBuilderTests.cs`, `SortBuilderTests.cs`) e `tests/Queryable.EntityFrameworkCore.Tests`
(execução contra SQLite in-memory, ver `SqliteInMemoryFixture.cs`). A cobertura proposta:

1. **Extração de caminho a partir de `Expression`.** Dado um seletor `u => u.Cpf.Value`, o
   `List<PropertyInfo>` extraído deve ser `[Cpf, Value]`, na ordem raiz→folha. Cobrir também
   seletor de um nível (`u => u.Nome`), seletor com conversão implícita (`UnaryExpression`
   desembrulhada) e os casos de rejeição: chamada de método, indexador e campo — cada um deve
   lançar exceção citando a expressão recebida.

2. **Mesclagem e precedência de alias (modo permissivo).** Configurar `For(u => u.Cpf.Value).As("cpf")`
   e verificar que o mapa resultante tem `cpf` apontando para `[Cpf, Value]` (não mais para `[Cpf]`),
   e que as demais entradas automáticas do tipo (`nome`, `perfil.descricao` etc.) permanecem
   inalteradas.

3. **Modo opt-in.** Configurar um tipo com `OnlyMapped()` e dois `For(...)`, e verificar que o
   mapa resultante contém exatamente essas duas entradas — nenhuma propriedade automática
   adicional aparece.

4. **Cache.** Chamar `IPropertyPathProvider.GetPaths<T>()` duas vezes para o mesmo tipo e verificar
   (por exemplo, com um contador de invocações instrumentado sobre `PathExtension.BuildPropertyPaths`,
   ou por identidade de referência do dicionário retornado) que a segunda chamada não refaz a
   varredura recursiva.

5. **Tradução real (SQLite in-memory).** Em `tests/Queryable.EntityFrameworkCore.Tests`, um teste
   que configura um tipo com value object colapsado (`For(u => u.Cpf.Value).As("cpf")`), monta um
   `FilterBuilder` com o provider configurado, aplica `?cpf=12345678900` contra um `DbContext` real
   apontando para SQLite in-memory, e verifica que o SQL gerado (ou o resultado da consulta) reflete
   corretamente `WHERE Cpf_Value = '12345678900'` (ou o nome de coluna que o mapeamento do EF Core
   produzir) — provando que o alias configurado sobrevive à tradução completa para SQL, não só à
   montagem da `Expression` em memória.

---

## 13. Alternativas consideradas e descartadas

- **(a) Novo atributo no nível da entidade com caminho em string** (ex.:
  `[QueryableAlias("cpf", "Cpf.Value")]` aplicado à classe `Usuario`, com o caminho como string
  livre). Resolveria o caso de colapso de value object sem o problema de "afeta todo mundo" do
  atributo atual em `Cpf.Value`, mas troca segurança de compilação por uma string solta: um refactor
  que renomeia `Cpf` para `Documento` não quebra a build, só quebra em runtime (ou silenciosamente,
  se o alias simplesmente deixar de casar com nenhuma propriedade). Continua exigindo anotação —
  ainda que na entidade em vez do VO — o que não atende ao pedido de "sem anotação no domínio".

- **(b) Source generator igual ao AuditLog.** Validação em tempo de compilação (erro de build se o
  seletor referenciar algo inválido) é uma propriedade atraente, mas é maquinário pesado — exige
  um projeto de analyzer/generator separado, empacotamento como analyzer Roslyn, e manutenção de
  compatibilidade entre versões do compilador. Além disso, **não elimina o custo principal** que
  esta proposta ataca na seção 7: a varredura de reflexão do mapa de caminhos continua acontecendo
  em runtime de qualquer forma, porque `PathExtension` opera sobre o `Type` real via reflexão, não
  sobre código gerado estaticamente. Pode ser considerado como otimização futura (validação adicional
  em build), mas não como substituto da configuração fluent em runtime.

- **(c) Derivar automaticamente de metadados do EF Core.** O EF Core já modela value object como
  *owned type* e conhece o mapeamento de coluna correspondente. Um complemento no pacote
  `Queryable.DynamicFilter.EntityFrameworkCore` (`src/Queryable.EntityFrameworkCore`) poderia ler
  `IModel`/`IEntityType` do `DbContext` e colapsar owned types automaticamente, sem exigir nenhuma
  configuração manual — o caso `Usuario.Cpf.Value → cpf` sairia de graça para quem já modela `Cpf`
  como owned type no EF. Esta é a alternativa **mais promissora para o futuro**, mas foi deixada
  fora do escopo inicial porque acopla a resolução de caminhos ao EF Core, enquanto o pacote núcleo
  (`Queryable.DynamicFilter`) hoje é independente de ORM. A arquitetura de `IPropertyPathProvider`
  proposta aqui deixa essa porta aberta deliberadamente: uma implementação adicional
  `EfCoreAwarePropertyPathProvider : IPropertyPathProvider`, registrada no pacote EF Core, poderia
  compor com (ou substituir) o provider baseado em configuração fluent, sem exigir mudança na
  interface nem nos builders.

---

## 14. Questões em aberto

- **Nome da classe base.** `QueryableConfiguration<T>` colide visualmente com o namespace raiz da
  lib (`Queryable`) e com o tipo `System.Linq.Queryable` do BCL — vale considerar um nome mais
  específico (ex.: `PropertyPathConfiguration<T>`, `QueryableMappingConfiguration<T>`) antes de
  fixar a API pública.
- **`As()` aceita múltiplos aliases?** Hoje a proposta assume um alias por `For(...)`. Cabe decidir
  se `As("cpf", "documento")` (múltiplos aliases apontando para o mesmo caminho) é um caso real o
  suficiente para entrar já na fase 2, ou se fica para depois.
- **`cpf.value` deve sumir quando `cpf` é reconfigurado?** No modo permissivo, ao configurar
  `For(u => u.Cpf.Value).As("cpf")`, a entrada automática antiga `cpf.value` (mesmo caminho,
  alias diferente) permanece por padrão (seção 8). Decidir se isso é desejável (mais um alias
  válido para o mesmo campo, inofensivo) ou se deveria ser removida automaticamente para evitar dois
  nomes públicos para o mesmo dado.
- **`OnlyMapped()` deveria ser o padrão para todo tipo que tem qualquer configuração?** Hoje a
  proposta trata `OnlyMapped()` como opt-in explícito mesmo para tipos já configurados (ter um
  `QueryableConfiguration<T>` não implica opt-in automático). Alternativa: qualquer tipo com uma
  classe de configuração já entraria em modo restrito por padrão, e `OnlyMapped()` deixaria de ser
  necessário. Tradeoff: mais seguro por padrão, mas rompe a garantia atual de "configurar um alias
  extra nunca restringe nada" — precisa de decisão explícita do time.
- **Onde mora a varredura de assembly** (`AddQueryableConfigurationsFromAssembly`) — no pacote
  núcleo (`Queryable`) ou em um pacote separado, e se ela pertence ao mesmo assembly do
  `IPropertyPathProvider` ou a um pacote de extensões de DI equivalente ao que hoje já existe em
  `ServiceCollectionExtensions.cs`.
