---
name: queryable-etapa
description: Use ao implementar uma etapa do docs/plano-implementacao.md neste repositório, cobrindo o ciclo completo até a entrega (implementação, testes, documentação, commits e integração), inclusive quando parte do trabalho for paralelizada em worktrees.
---

# Skill: queryable-etapa

## Ponto de partida

`docs/plano-implementacao.md` tem as etapas 0 a 6, cada uma com versão alvo, dependências e critério de pronto. Leia a etapa alvo ANTES de planejar. As propostas que originam as etapas estão em `docs/proposta-filtros-compostos.md` e `docs/proposta-configuracao-fluent.md`.

As propostas costumam ter uma seção de "questões em aberto" — são decisões de API pública. Levante-as e pergunte todas de uma vez, antes de começar a implementar, em vez de travar no meio da etapa. Exemplo real: na etapa 3, decidir antecipadamente que `OnlyMapped()` seria explícito e que os aliases automáticos continuariam coexistindo com os configurados definiu a implementação inteira, e as duas decisões juntas transformaram uma mudança arriscada em adoção incremental.

## O ciclo que funcionou

A sequência que se repetiu nas etapas 3 a 6, nessa ordem: **implementar → passada de endurecimento → documentar → commitar em partes → gate → tag**.

A "passada de endurecimento" acontece ao receber o relatório do agente de implementação, antes de qualquer commit: procure ativamente pelas ressalvas que o próprio agente levantou e resolva-as. Relatório de subagente costuma conter o próprio defeito, declarado de passagem — dois casos reais: um agente relatou ter duplicado uma lista de operadores "para evitar dependência circular" (virou refactor de centralização antes do commit) e ter validado o model binder "por inspeção" (virou teste antes do commit). Trate ressalva declarada como pendência, não como nota de rodapé.

"Validado por inspeção" não é validação: se um agente disser que conferiu algo lendo o código em vez de executando, trate como não verificado — em especial para pontos de entrada HTTP (model binder), tradução para SQL e comportamento dependente de ambiente. Exija teste.

## A armadilha do worktree (custou tempo, não repita)

Ao criar worktree para trabalho paralelo, ele pode nascer apontando para um commit **antigo** — a história deste repositório teve uma reorganização (projeto movido da raiz para `src/`), e um worktree criado a partir do commit errado não tem `src/`, `tests/` nem `docs/`.

**Sempre confirme a base antes de escrever qualquer código:**

```bash
git log --oneline -1
git merge-base --is-ancestor <commit-esperado> HEAD && echo OK
```

Se a base estiver errada, corrija (`git checkout -b <branch> main` ou `git merge --ff-only main`) antes de começar.

Além disso: **worktree só enxerga estado commitado.** Arquivo não versionado no repositório principal não existe no worktree. Commite antes de paralelizar.

## Compatibilidade de API pública

Padrão que se repetiu nas etapas 1, 2, 4 e 6: toda adição a uma API pública já publicada tem que ser aditiva.

- Método novo em interface pública ⇒ **default interface method**, senão quebra quem já implementou a interface.
- Parâmetro novo ⇒ **opcional**, ou sobrecarga.
- Injeção nova em classe instanciável ⇒ **preserve o construtor sem parâmetros** — existe código, inclusive nos testes, que faz `new FilterBuilder()`.

Se algum teste existente precisar mudar para a etapa passar, isso é sinal de quebra de compatibilidade: conserte o código de produção, não o teste.

## Fonte única de verdade

Quando dois caminhos de código precisam concordar, eles vão divergir se forem cópias paralelas. Aconteceu duas vezes neste repositório: a conversão de valores entre `ConvertValue` e `BuildInExpression` (resolvido centralizando em `ConvertScalar`), e a lista de operadores entre `FilterBuilder` e o parser da mini-linguagem. Regra: faça um caminho **consumir** o outro, ou extraia uma fonte única — nunca deixe cópias paralelas "por enquanto".

Ao centralizar, cuidado com propriedades implícitas na ordem: a lista de operadores precisa ficar ordenada por **comprimento decrescente**, senão o sufixo `__gte` casa primeiro com `gt` e o campo é fatiado errado. Propriedade não óbvia como essa tem que virar teste, não comentário.

## O que NÃO paralelizar

As etapas 1 e 2 alteram o MESMO arquivo, `src/Queryable/Builders/FilterBuilder.cs` — uma o construtor e a linha que obtém o mapa de caminhos, outra o corpo do `BuildPredicate`. Worktree não resolve isso; só adia o conflito para o merge. São sequenciais.

O par que realmente paraleliza é etapa 3 (configuração/caminhos) com etapa 4 (árvore de filtro/JSON).

Worktree só compensa para trabalho **realmente paralelo em arquivos disjuntos**. Nas etapas 5 e 6, sequenciais por dependência, trabalhar direto na `main` foi mais simples e evitou de novo a armadilha da base obsoleta — worktree é a exceção para paralelismo real, não o padrão default. Regra que se manteve o tempo todo: nunca dois agentes escrevendo no mesmo diretório ao mesmo tempo. Documentação e testes podem correr em paralelo se, e só se, estiverem em arquivos diferentes.

## Branch e integração

- Branch por etapa: `feat/etapa-N-<slug>`.
- Histórico **linear**: integre com `git merge --ff-only`; se o branch divergiu, `git rebase main <branch>` antes.
- Remova worktrees e branches auxiliares ao final (`git worktree remove --force`, `git worktree prune`, `git branch -d`). Use `-d` minúsculo: se o git recusar, a integração não ficou como esperado.

## Commit

Convenção de `AGENTS.md`: `<ícone> Queryable - <categoria>: <descrição>`. Até a release `v10.3.0` (commits até `77e1f07`), o prefixo era grafado `Qeryable` (sem o "u") por convenção antiga do `AGENTS.md` — corrigido para `Queryable` a partir daqui; o histórico anterior não foi reescrito (decisão do usuário), então commits antigos com `Qeryable` são esperados e não devem ser usados como referência para commits novos. Categorias: `feat` ✨, `bugfix` 🐛, `refactor` ♻️, `perf` 🚀, `build` ⚙️, `ci` 🔧, `test` 🧪, `docs` 📚, `chore` 🧹, `deps` 📦, `remove` 🔥, `style` 🎨.

**Nunca** inclua trailer `Co-Authored-By:`.

Feche cada etapa em 2 a 4 commits, um por preocupação: `refactor` separado de `feat`, `test` separado de `docs`. Isso mantém o histórico útil e cada commit compilável sozinho. Quando um refactor viabiliza a feature (por exemplo, centralizar os operadores antes de o parser passar a consumi-los), ele vem **antes**, em commit próprio.

## Verificação

Durante a etapa, rode só o escopo que prova a mudança:

```bash
dotnet build src/Queryable/Queryable.csproj -c Release
dotnet test tests/Queryable.Tests/Queryable.Tests.csproj -c Release
```

O gate completo (build da solução + as duas suítes) roda uma vez, no fim, antes de integrar. Existem duas suítes: `tests/Queryable.Tests` (núcleo) e `tests/Queryable.EntityFrameworkCore.Tests` (contra SQLite in-memory, que é o único caminho que pega erro de tradução para SQL — `List<T>.AsQueryable()` mascara isso porque executa em memória).

Quando o usuário perguntar se algo funciona, não responda lendo o código — monte um projeto descartável **fora do repositório**, referencie o **pacote publicado** e execute. Isso já corrigiu suposições três vezes: alias de navegação profunda, conversão de `decimal` dependente de cultura, e suporte a coleções (a verificação revelou que `.Count` funciona porque é propriedade, enquanto `.Any(...)` não funciona porque é método — nenhuma leitura do código teria deixado isso óbvio). Para qualquer coisa que gere `Expression`, verifique contra EF Core com SQLite, nunca só `List<T>.AsQueryable()`, pelo mesmo motivo que vale para as suítes: LINQ to Objects executa em memória o que o provider relacional não traduz.

## Ao terminar a etapa

Atualize `CLAUDE.md` e o `README.md` da raiz se a etapa mudou comportamento ou desfez alguma limitação documentada. O trabalho principal aqui não é registrar o que foi feito — é caçar ativamente afirmações que a etapa tornou falsas. Aconteceu em todas as etapas: "não há proteção contra ciclos", "`in` não funciona com `Guid`", "não existe projeto de teste", "a query string só combina por AND", "erro de sintaxe vira 500". Documentação que afirma limitação já resolvida é pior que documentação ausente.

Atenção especial ao `README.md` da raiz: ele é **empacotado no NuGet** e é o que o consumidor lê primeiro. Uma correção que fique só em `docs/` não chega até ele — já aconteceu com a ressalva de acoplamento do `IProjectable`.

Depois, para publicar, use a skill `queryable-release`.
