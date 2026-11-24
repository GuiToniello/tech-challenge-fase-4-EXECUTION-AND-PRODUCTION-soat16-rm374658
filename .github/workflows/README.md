# Pipelines (GitHub Actions)

As pipelines deste repositório cuidam só do Execução e Produção:

- **Aplicação** (`server/`): build, testes e a imagem do Execução e Produção no ECR, no repositório `techchallenge-oficina-execution`, criado manualmente.
- **Banco** (`DB/infra`): o RDS PostgreSQL do Execução e Produção, a tabela DynamoDB da linha do tempo da produção ([ADR-025](../../docs/adr/ADR-025-Linha-do-Tempo-da-Producao-no-DynamoDB.md)) e a política IAM dela, anexada ao usuário IAM do Execução e Produção criado pelo MANAGEMENT.
- **Manifests** (`kubernetes/k8s`): namespace `execution`, ConfigMap, Secret, Deployment, Service `NodePort` 30083 e HPA.

A rede, o EKS, o API Gateway (rota `/execution`), o NLB, o Datadog e as filas SQS (com o usuário IAM do Execução e Produção e a chave dele no SSM) ficam no repo [MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658), que precisa estar de pé antes. O repo [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658) também precisa subir antes, porque o OS Service consome os eventos que este serviço publica. O padrão é o mesmo das [pipelines do MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/.github/workflows/README.md), do [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/.github/workflows/README.md) e do [BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/.github/workflows/README.md), com menos partes.

```
.github/workflows/
  _build-test.yml   (workflow_call)      - restore, build e testes da solution .slnx
  _build-push.yml   (workflow_call)      - docker compose build + push da imagem do Execução e Produção no ECR
  _terraform.yml    (workflow_call)      - validate/plan/apply/destroy do DB/infra
  _k8s-apply.yml    (workflow_call)      - kustomize offline (validate) ou kubectl apply -k no EKS (apply)
  bootstrap.yml     (workflow_dispatch)  - sobe o Execução e Produção, na ordem
  deploy.yml        (pull_request, push) - PR: valida tudo; push na main: aplica só o que mudou, na ordem
  destroy.yml       (workflow_dispatch)  - remove o namespace, o RDS e a tabela DynamoDB do Execução e Produção
  k8s-apply.yml     (workflow_dispatch)  - reaplica os manifests e reinicia a API
```

**Ordem com os outros repositórios:**
- **Para subir:** Bootstrap do MANAGEMENT → Bootstrap do OS-SERVICE → Bootstrap deste repo (o OS Service precisa consumir `DiagnosticoIniciado` e `ExecucaoFinalizada` antes de este serviço publicá-los) → Deploy do [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O Bootstrap do [BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658) roda a qualquer momento depois do MANAGEMENT. O MANAGEMENT vem antes também porque o `DB/infra` daqui anexa a política do DynamoDB ao usuário IAM do Execução e Produção, criado por ele. Se o MANAGEMENT recriar esse usuário, a política some junto: rode o Bootstrap deste repo de novo (o Deploy só reaplica o `DB/infra` quando os arquivos dele mudam).
- **Para derrubar:** Destroy do LAMBDA, do OS-SERVICE, do BILLING e deste repo (em qualquer ordem) → Destroy do MANAGEMENT. O Destroy do MANAGEMENT (job `execution-check`) falha se o RDS `techchallenge-oficina-execution-postgres` ou o SG `techchallenge-oficina-execution-rds-sg` ainda existirem.

## 1. Workflows reutilizáveis

### `_build-test.yml`
- `dotnet restore`, `dotnet build -c Release` e `dotnet test -c Release` em `server/TechChallenge.Oficina.Execution.slnx`, com .NET 10.
- Os testes usam banco em memória e mocks (SQS e DynamoDB): não precisam de AWS, banco nem Docker.
- No Linux, o `UseArtifactsOutput` do `server/Directory.Build.props` (criado por causa dos caminhos longos no Windows) só muda o lugar da saída do build: `server/artifacts/`.

### `_build-push.yml`
- Recebe o input `image-tag`; o Bootstrap e o Deploy passam sempre `latest`, a tag dos manifests.
- Falha fora da `main`, confere a variable `ECR_REGISTRY`, faz login no ECR e roda `docker compose build` e `docker compose push` do serviço `execution-api` (imagem `techchallenge-oficina-execution`).

### `_terraform.yml`
- `working-directory`: só `DB/infra`. `command`: `validate`, `plan`, `apply` ou `destroy`.
- Falha em apply/destroy fora da `main`. Terraform 1.15.8, `init` (com `-backend=false` no validate, que não usa AWS), `fmt -check -recursive` e `validate`; depois, `plan -lock=false` ou `apply`/`destroy -auto-approve -lock-timeout=15m`.
- Recebe `TF_VAR_rds_password` do secret `RDS_PASSWORD`.
- O `DB/infra` tem, no mesmo state, o RDS (`rds.tf`), a tabela DynamoDB da linha do tempo da produção (`dynamodb.tf`) e a política inline `dynamodb-historico-producao` (`iam.tf`), anexada ao usuário IAM `techchallenge-oficina-execution-sqs`.

| Pasta | State (bucket `terraform-state-soat16`) |
|---|---|
| `DB/infra` | `techchallenge-oficina/fase-4/execution-database.tfstate` |

### `_k8s-apply.yml`
- **validate:** copia `kubernetes/k8s/.env.example` para `.env` e roda `kubectl kustomize kubernetes/k8s`, offline.
- **apply:**
  1. Bloqueio fora da `main` e checkout da ponta da `main`.
  2. Confere `EKS_CLUSTER_NAME`, `RDS_INSTANCE_IDENTIFIER`, `RDS_DATABASE`, `RDS_USERNAME` e `RDS_PASSWORD` e roda `aws eks update-kubeconfig` (o EKS é do MANAGEMENT).
  3. Descobre o endpoint do RDS pelo `RDS_INSTANCE_IDENTIFIER`. Se o RDS não existir, falha pedindo o Bootstrap deste repo.
  4. Gera o `kubernetes/k8s/.env` com a connection string. Os valores entram por variável de ambiente, para caracteres como `$` na senha não serem interpretados.
  5. Lê do SSM a chave do usuário IAM deste serviço (`/techchallenge-oficina/sqs/execution/access-key-id` e `/secret-access-key`, criados pelo `SQS/infra` do MANAGEMENT), mascara os valores e os acrescenta ao `.env` como `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY`. A mesma chave serve às filas SQS e à tabela DynamoDB da linha do tempo (no log, é o passo "Adicionar a chave do usuário IAM do Execução e Produção (SQS e DynamoDB) ao kubernetes/k8s/.env"). Se os parâmetros não existirem, falha pedindo o Bootstrap do MANAGEMENT.
  6. `kubectl apply -k kubernetes/k8s` e, se `restart-pods`, `kubectl rollout restart` no namespace `execution`.
- O Secret `execution-api-secrets` não tem hash no nome: trocar a senha ou a chave do usuário IAM só chega aos pods com restart.

## 2. `bootstrap.yml`

- **Gatilho:** `workflow_dispatch`, a partir da `main`.
- **Pré-requisitos:** Bootstraps do MANAGEMENT e do OS-SERVICE.
- **Jobs, nesta ordem:** `build-test` → `terraform-db` (RDS, tabela DynamoDB e política IAM) → `build-push` → `k8s-apply` (com restart).
- Use na primeira criação, para recriar o Execução e Produção depois de um Destroy (deste repo ou do MANAGEMENT), para reaplicar a política do DynamoDB depois que o MANAGEMENT recriar o usuário IAM do Execução e Produção ou para reaplicar a HEAD da `main`.

## 3. `deploy.yml`

| Evento | Jobs | Observação |
|---|---|---|
| `pull_request` → `main` | `build-test`, `validate-db`, `validate-k8s` | **Checks obrigatórios.** Não usam AWS |
| | `plan-db` | Informativo. Precisa do cluster do MANAGEMENT de pé (o DB acha a VPC pelo EKS). Pulado em PR de fork |
| `push` na `main` | `build-test` e `changes` (`dorny/paths-filter`) | Filtros: `app` (`server/**`, `docker-compose.yml`, `_build-test.yml` e `_build-push.yml`), `db` (`DB/infra/**` e `_terraform.yml`) e `k8s` (`kubernetes/k8s/**` e `_k8s-apply.yml`) |
| | `apply-db` | Se `db` mudou |
| | `build-push` | Se `app` mudou. Espera os testes e o DB |
| | `apply-k8s` (com restart) | Se qualquer parte mudou (imagem nova, manifests ou RDS recriado) |

- **O PR não usa filtro de paths**, para os checks obrigatórios não ficarem *pending* em PRs só de documentação.
- Os pushes na `main` rodam um de cada vez, na ordem; num PR, um run novo cancela o anterior.
- **Custo:** o primeiro push na `main` marca todos os arquivos como alterados e aplica tudo, inclusive o RDS. Se os secrets da AWS já estiverem configurados, ele cria o RDS do Execução e Produção (cobrado por hora); sem eles, os applies falham na credencial e não criam nada. Com o MANAGEMENT desligado, o `apply-db` e o `apply-k8s` falham, porque não há cluster.
- **Não use "Re-run" em um Deploy antigo:** ele publicaria `latest` de um commit antigo. Para reaplicar a HEAD, use o Bootstrap.
- O `apply-db` só roda quando o `DB/infra` muda. Se o MANAGEMENT recriar o usuário IAM do Execução e Produção, a política do DynamoDB só volta com o Bootstrap.

## 4. `destroy.yml`

- **Gatilho:** `workflow_dispatch` com o input obrigatório `confirm`.

| Job | Quando roda | O que faz |
|---|---|---|
| `reject` | `confirm` ≠ `destroy` ou branch ≠ `main` | **Falha** com erro, em vez de terminar verde sem ter destruído nada |
| `gate` | `confirm` = `destroy` na `main` | `environment: destroy`: aprovação manual |
| `k8s-delete` | depois do `gate` | Se o cluster do MANAGEMENT existir, apaga o namespace `execution` (e tudo dentro dele); se não existir, só avisa |
| `terraform-destroy-db` | depois do `k8s-delete` | Destroy do `DB/infra`: RDS, tabela DynamoDB e política IAM. **Os dados do RDS do Execução e Produção (a fila e as datas de cada OS) e a linha do tempo da produção são perdidos** |

- Rode **antes** do Destroy do MANAGEMENT: o `DB/infra` encontra a VPC pelo cluster EKS, o MANAGEMENT só derruba a rede depois que este RDS sai, e a política da tabela DynamoDB está no usuário IAM do Execução e Produção, criado por ele.
- Configure o environment `destroy` antes do primeiro uso, com *Required reviewers* e *Deployment branches* restrito a `main`.

## 5. `k8s-apply.yml`

- **Gatilho:** `workflow_dispatch`, com o input `restart-pods` (padrão `true`).
- **Quando usar:** depois que o MANAGEMENT recriar as filas e a chave do usuário IAM do Execução e Produção, depois de ligar ou desligar o Datadog no MANAGEMENT, ou para reiniciar a API.
- **Para trocar `RDS_PASSWORD`, use o Bootstrap**, que aplica a senha nova no RDS antes de gravá-la no Secret. **Se o MANAGEMENT recriar o próprio usuário IAM do Execução e Produção**, também use o Bootstrap: a política do DynamoDB precisa ser aplicada de novo no usuário novo.

---

## Secrets e Variables

Configure em **Settings → Secrets and variables → Actions**, no nível do repositório:

| Nome | Tipo | Uso |
|---|---|---|
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Secret | Todos os jobs que acessam a AWS (o mesmo usuário IAM `terraform` do MANAGEMENT) |
| `RDS_PASSWORD` | Secret | Senha master do RDS do Execução e Produção: `_terraform.yml` (`TF_VAR_rds_password`) e `_k8s-apply.yml` (connection string). Use de 8 a 128 caracteres ASCII imprimíveis, sem `/`, `'`, `"`, `@`, espaço e `;` |
| `AWS_REGION` | Variable | Todos (`us-east-1`) |
| `ECR_REGISTRY` | Variable | `_build-push.yml` (`903936907231.dkr.ecr.us-east-1.amazonaws.com`) |
| `EKS_CLUSTER_NAME` | Variable | `_k8s-apply.yml` e `destroy.yml` (`techchallenge-oficina-eks`) |
| `RDS_INSTANCE_IDENTIFIER` | Variable | `_k8s-apply.yml` (`techchallenge-oficina-execution-postgres`) |
| `RDS_DATABASE` / `RDS_USERNAME` | Variable | `_k8s-apply.yml` (`oficina_execution` / `sa`) |

Não há secret para o SQS, o DynamoDB nem o Datadog: a chave do usuário IAM do Execução e Produção (filas e tabela) vem do SSM, e o Datadog é configurado no MANAGEMENT.

**Permissões IAM do usuário `terraform`:** as mesmas usadas pelo MANAGEMENT. O state fica em `techchallenge-oficina/fase-4/execution-database.tfstate`, coberto pela permissão em `techchallenge-oficina/fase-4/*`, e o push no ECR usa a permissão em `repository/techchallenge-oficina-*`. O `DB/infra` também cria e apaga a tabela DynamoDB e a política inline do usuário do Execução e Produção: precisa de permissões no DynamoDB e de `iam:PutUserPolicy` (com `iam:GetUserPolicy` e `iam:DeleteUserPolicy`); hoje o usuário é administrador. O apply dos manifests também lê os parâmetros do SSM (`ssm:GetParameter`) e consulta o RDS pelo identifier (`rds:DescribeDBInstances`).

**Environment `destroy`** (Settings → Environments): crie antes do primeiro Destroy, com *Required reviewers* e *Deployment branches* restrito a `main`.

**Proteção da `main`** (Settings → Rules → Rulesets): exija Pull Request e os checks abaixo, que aparecem na lista depois que o primeiro PR roda.
- `build-test / build-test`
- `validate-db / terraform`
- `validate-k8s / kubectl`

Os callers passam os secrets aos workflows reutilizáveis com `secrets: inherit` (o `_build-test.yml` não usa secrets), e todos os workflows declaram `permissions: contents: read`.
