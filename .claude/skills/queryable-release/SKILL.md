---
name: queryable-release
description: Use ao publicar uma nova versão dos pacotes Queryable.DynamicFilter no NuGet, ou ao criar/empurrar tags de release neste repositório.
---

# Skill: queryable-release

## Antes de qualquer coisa

- **Publicação no nuget.org é irreversível.** Não existe apagar versão publicada — só *delist*, que esconde da busca mas mantém instalável por quem fixar o número. Confirme com o usuário antes de empurrar qualquer tag.
- Consulte a última versão publicada antes de escolher o número:
  ```bash
  curl -s https://api.nuget.org/v3-flatcontainer/queryable.dynamicfilter/index.json
  ```
  Existem DOIS pacotes: `queryable.dynamicfilter` e `queryable.dynamicfilter.entityframeworkcore`. Eles saem sempre em par, com a mesma versão.

## A armadilha das duas push URLs (a mais importante)

O remote `origin` tem **duas** push URLs: GitHub (`SamuelGFDias/Queryable`, pessoal) e GitLab (`ses-rj/desenvolvimento/libs/dotnet/queryable`, corporativo). Portanto:

- `git push origin main` → vai para os DOIS. É o desejado: mantém a base corporativa atualizada.
- `git push origin <tag>` → **também vai para os dois**, e isso NÃO é desejado. A publicação só deve rodar no GitHub pessoal, e o `.gitlab-ci.yml` delega a um pipeline compartilhado da SES (`ses-rj/ci-cd/pipelines`, `simple-ci/manifests/v1/v1.yml`) cujo comportamento em tag é desconhecido.
- A tag vai por URL explícita: `git push git@github.com:SamuelGFDias/Queryable.git <tag>`
- **Nunca** use `git push --tags`.

## Onde a tag aponta

A versão vem do **MinVer**, derivada da tag `v*` alcançável. Não existe `<Version>` nos csproj.

Quando o trabalho está organizado em etapas (ver `docs/plano-implementacao.md`), cada etapa tem sua versão e a tag aponta para o **commit daquela etapa**, não necessariamente para o `HEAD`. Tagear sempre o HEAD colapsaria várias etapas numa release só.

Use tag anotada: `git tag -a vX.Y.Z <commit> -m "<descrição>"`, e confirme com `git rev-list -n 1 vX.Y.Z`.

## Pré-requisito de autenticação

O workflow (`.github/workflows/publish.yml`) usa trusted publishing por OIDC (`NuGet/login@v1`, user `samueldias21`), sem secret. Depende de:

- policy `queryable_dynamic_filter` no nuget.org (owner `samueldias21` → repo `SamuelGFDias/Queryable`, workflow `publish.yml`, environment `production`);
- environment `production` existindo no GitHub, porque o job declara `environment: production` e a policy valida esse claim.

Se um dos dois lados mudar, o outro tem que mudar junto.

## Gate obrigatório antes de taggear

```bash
dotnet build -c Release      # 0 erros, 0 avisos
dotnet test -c Release --no-build
```

Se a tag apontar para um commit que NÃO é o HEAD, valide **aquele commit** num worktree temporário destacado — ele vai virar uma versão publicada:

```bash
git worktree add --detach /tmp/verify <commit>
# build + test dentro dele
git worktree remove --force /tmp/verify
```

## Pré-voo: inspecionar o que seria publicado

Crie a tag **localmente**, empacote, inspecione, e **apague a tag** antes de decidir:

```bash
git tag vX.Y.Z <commit>
dotnet build -c Release
dotnet pack src/Queryable/Queryable.csproj -c Release --no-build --output ./artifacts
dotnet pack src/Queryable.EntityFrameworkCore/Queryable.EntityFrameworkCore.csproj -c Release --no-build --output ./artifacts
git tag -d vX.Y.Z
```

Inspecione os dois `.nupkg` (são zip) confirmando: versão correta; `README.md` dentro do pacote e `<readme>` no nuspec (os DOIS pacotes têm README, vindo da raiz via `Directory.Build.props`); e as dependências — o pacote EF deve depender de `Queryable.DynamicFilter` na versão exata da release. Apague `./artifacts` depois.

## Armadilha do pack

`dotnet pack` sozinho falha com **`NU5026`**. `GeneratePackageOnBuild=true` quebra a ordem build→pack. Sempre `dotnet build -c Release` antes e `--no-build` no pack.

## Ordem de execução

1. Gate completo.
2. Pré-voo e inspeção.
3. Confirmar com o usuário.
4. `git push origin main`.
5. Criar a tag no commit certo.
6. Empurrar a tag SÓ para o GitHub.
7. Acompanhar o workflow (`gh` pode não estar disponível; nesse caso, peça ao usuário para conferir em github.com/SamuelGFDias/Queryable/actions).
8. Confirmar no nuget.org antes de partir para a próxima tag.

Publique uma tag por vez, confirmando a anterior. Se a primeira falhar no OIDC, a falha ocorre antes de publicar e é recuperável apagando a tag.
