---
name: queryable-etapa
description: Use ao implementar uma etapa do docs/plano-implementacao.md neste repositório, especialmente quando for paralelizar trabalho em worktrees.
---

# Skill: queryable-etapa

## Ponto de partida

`docs/plano-implementacao.md` tem as etapas 0 a 6, cada uma com versão alvo, dependências e critério de pronto. Leia a etapa alvo ANTES de planejar. As propostas que originam as etapas estão em `docs/proposta-filtros-compostos.md` e `docs/proposta-configuracao-fluent.md`.

## A armadilha do worktree (custou tempo, não repita)

Ao criar worktree para trabalho paralelo, ele pode nascer apontando para um commit **antigo** — a história deste repositório teve uma reorganização (projeto movido da raiz para `src/`), e um worktree criado a partir do commit errado não tem `src/`, `tests/` nem `docs/`.

**Sempre confirme a base antes de escrever qualquer código:**

```bash
git log --oneline -1
git merge-base --is-ancestor <commit-esperado> HEAD && echo OK
```

Se a base estiver errada, corrija (`git checkout -b <branch> main` ou `git merge --ff-only main`) antes de começar.

Além disso: **worktree só enxerga estado commitado.** Arquivo não versionado no repositório principal não existe no worktree. Commite antes de paralelizar.

## O que NÃO paralelizar

As etapas 1 e 2 alteram o MESMO arquivo, `src/Queryable/Builders/FilterBuilder.cs` — uma o construtor e a linha que obtém o mapa de caminhos, outra o corpo do `BuildPredicate`. Worktree não resolve isso; só adia o conflito para o merge. São sequenciais.

O par que realmente paraleliza é etapa 3 (configuração/caminhos) com etapa 4 (árvore de filtro/JSON).

## Branch e integração

- Branch por etapa: `feat/etapa-N-<slug>`.
- Histórico **linear**: integre com `git merge --ff-only`; se o branch divergiu, `git rebase main <branch>` antes.
- Remova worktrees e branches auxiliares ao final (`git worktree remove --force`, `git worktree prune`, `git branch -d`). Use `-d` minúsculo: se o git recusar, a integração não ficou como esperado.

## Commit

Convenção de `AGENTS.md`: `<ícone> Queryable - <categoria>: <descrição>`. Até a release `v10.3.0` (commits até `77e1f07`), o prefixo era grafado `Qeryable` (sem o "u") por convenção antiga do `AGENTS.md` — corrigido para `Queryable` a partir daqui; o histórico anterior não foi reescrito (decisão do usuário), então commits antigos com `Qeryable` são esperados e não devem ser usados como referência para commits novos. Categorias: `feat` ✨, `bugfix` 🐛, `refactor` ♻️, `perf` 🚀, `build` ⚙️, `ci` 🔧, `test` 🧪, `docs` 📚, `chore` 🧹, `deps` 📦, `remove` 🔥, `style` 🎨.

**Nunca** inclua trailer `Co-Authored-By:`.

## Verificação

Durante a etapa, rode só o escopo que prova a mudança:

```bash
dotnet build src/Queryable/Queryable.csproj -c Release
dotnet test tests/Queryable.Tests/Queryable.Tests.csproj -c Release
```

O gate completo (build da solução + as duas suítes) roda uma vez, no fim, antes de integrar. Existem duas suítes: `tests/Queryable.Tests` (núcleo) e `tests/Queryable.EntityFrameworkCore.Tests` (contra SQLite in-memory, que é o único caminho que pega erro de tradução para SQL — `List<T>.AsQueryable()` mascara isso porque executa em memória).

## Ao terminar a etapa

Atualize `CLAUDE.md` e o `README.md` da raiz se a etapa mudou comportamento ou desfez alguma limitação documentada. Documentação que afirma limitação já resolvida é pior que documentação ausente. Depois, para publicar, use a skill `queryable-release`.
