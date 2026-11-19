# ADR-023: Execução e Produção, Fila de Execução e Status por Eventos

**Status**: Aceita (fase 4). Tira do OS Service as rotas de diagnóstico, de início da execução e de finalização ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)) e amplia a [ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md): quarta fila, regra de publicação reescrita (cada serviço publica só para quem consome os eventos dele) e os eventos `DiagnosticoIniciado` e `ExecucaoFinalizada`

**Resumo**:
A fila de produção da oficina, o início do diagnóstico e o início e o fim da execução saem do OS Service e viram o **Execução e Produção** (`execution-api`), um microsserviço com banco, namespace no Kubernetes e pipelines próprios. O status da OS continua sendo só do OS Service: o Execução e Produção guarda uma cópia dele, alimentada pelo `OrdemServicoStatusAlterado`, e avisa o OS Service pelos eventos `DiagnosticoIniciado` e `ExecucaoFinalizada`. A OS entra na fila de execução quando o OS Service a leva para `EmExecucao`, depois do `PagamentoAprovado` do Billing. Os serviços continuam conversando só por eventos no Amazon SQS (ADR-020).

> As ADRs e RFCs são numeradas em sequência única entre os repositórios [MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658), [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), [BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658), EXECUTION (este) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice das decisões está no [docs/arquitetura.md do MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- Depois do OS Service (ADR-019) e do Billing ([ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md)), o trabalho da oficina ainda era feito por rotas de status do OS Service: `POST /api/ordens-servico/{id}/em-diagnostico`, `/em-execucao` (só para a OS sem orçamento) e `/finalizar`.
- Com orçamento, a OS entra em `EmExecucao` pelo pagamento aprovado (`PagamentoAprovado`): esse status passou a marcar o pagamento, e não o início do trabalho. Não havia registro de quando a oficina começa a execução, nem uma fila do que o mecânico tem para fazer.
- A lista `GET /api/ordens-servico/ordenadas` do OS Service acompanha o ciclo de vida das OS em aberto: junta o que depende do cliente (`AguardandoAprovacao`) com o que depende da oficina.
- As regras da ADR-020 continuam: um banco por serviço, nenhum serviço lê o banco do outro e não há HTTP entre as APIs.
- O cluster é pequeno (2 nós `t3.small`, [ADR-016](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-016-Escalabilidade-com-HPA.md)), e o custo precisa ficar perto de zero.

**Decisão**:

1. **Fronteiras**:
   - **Execução e Produção** (este repositório): o chão de oficina. A fila de produção, a consulta de uma OS na produção, o início do diagnóstico, o início da execução e a finalização, com a data de cada passo.
   - **OS Service** (repo OS-SERVICE): o ciclo de vida da OS: abrir, atualizar, excluir, consultar (inclusive a lista por prioridade `/ordenadas`, o acompanhamento com o histórico de status e a lista por cliente), entregar e os indicadores. É o **único** serviço que muda o status da OS.
   - **Billing** (repo BILLING): o orçamento e o pagamento. Continua dono da passagem de `AguardandoAprovacao` (3) para `EmExecucao` (4), pelo `PagamentoAprovado`.
   - **Management** (repo MANAGEMENT): cadastros, estoque (debitado no pagamento aprovado), validação da OS, e-mail de mudança de status e a infraestrutura compartilhada (rede, EKS, API Gateway, NLB, Datadog e filas SQS).
   - As rotas `/em-diagnostico`, `/em-execucao` e `/finalizar` saíram do OS Service. O diagnóstico e a finalização passaram para cá e chegam ao OS Service por evento. A execução só começa pelo `PagamentoAprovado`, então toda OS passa pelo orçamento e pelo pagamento antes de ser executada. Assim, o trabalho do mecânico evolui e é publicado sem mexer no ciclo de vida da OS, e nenhuma regra fica nos dois serviços.

2. **Banco próprio**: RDS PostgreSQL `techchallenge-oficina-execution-postgres` (banco `oficina_execution`), criado em `DB/infra`, nas subnets privadas da rede do MANAGEMENT. A única tabela é criada pelas migrations do serviço, no startup:

   | Tabela | O que guarda | Colunas |
   |---|---|---|
   | `execucoes` | Um item por OS (o `id` é o da OS) | `id`, `cliente_id`, `veiculo_id`, `servicos` (`text[]`, só os nomes), `status` (cópia do status do OS Service, com índice), `criado_em`, `diagnostico_iniciado_em`, `entrou_na_fila_em`, `execucao_iniciada_em`, `finalizada_em` e `removida_em` |

   Não há chaves estrangeiras para os cadastros, que ficam no banco da Management. A coluna de sistema `xmin` do PostgreSQL controla a concorrência (item 8).

3. **Status da OS como espelho, alimentado por eventos**:
   - O status do item só muda pelo `OrdemServicoStatusAlterado` do OS Service, nunca pelas rotas daqui. As rotas gravam datas e publicam eventos; o OS Service muda o status ao consumi-los e publica o `OrdemServicoStatusAlterado`, que atualiza a cópia (consistência eventual).
   - Os dados vêm do `OrdemServicoValidada` (Management): cliente, veículo e os nomes dos serviços. Ele nunca mexe no status nem nas datas; se a OS ainda não é conhecida aqui, ela começa como `Recebida`.
   - O `OrdemServicoValidada` (Management) e o `OrdemServicoStatusAlterado` (OS Service) saem de serviços diferentes, sem ordem entre si. Se o status chega primeiro, o item é criado só com ele, e a validação completa os dados depois, como no Billing (ADR-021).
   - **Repetir a ação recupera a publicação:** não há *transactional outbox*, e a publicação pode falhar depois de gravar (ADR-020, item 8). Enquanto o OS Service não confirma, isto é, enquanto a cópia continua `Recebida` ou `EmExecucao`, chamar de novo `iniciar-diagnostico` ou `finalizar` mantém a data da primeira chamada e publica o evento de novo. Um evento repetido encontra a OS já no status novo e é ignorado pelo OS Service.

4. **Entrada na fila de execução pelo status**: a OS entra na fila de execução quando a cópia do status vira `EmExecucao` (4). Isso acontece quando o OS Service consome o `PagamentoAprovado` do Billing (3 → 4) e publica o `OrdemServicoStatusAlterado`. A data da primeira entrada fica em `EntrouNaFilaEm`. Este serviço não consome o `PagamentoAprovado`, e o Billing não publica na fila dele: a passagem 3 → 4 continua sendo do Billing e do OS Service, e o status tem uma fonte só.

5. **Nenhum status novo na OS, e etapas internas**: os oito status da OS continuam os mesmos nos quatro serviços. O detalhe da oficina fica nas etapas deste serviço, calculadas a partir da cópia do status e das datas (não são gravadas):

   | Etapa | Quando |
   |---|---|
   | `AguardandoDiagnostico` (1) | Status `Recebida`, sem diagnóstico iniciado |
   | `EmDiagnostico` (2) | Status `Recebida` com o diagnóstico já iniciado aqui, ou status `EmDiagnostico` |
   | `AguardandoAprovacao` (3) | Status `AguardandoAprovacao`: o orçamento está com o cliente |
   | `AguardandoExecucao` (4) | Status `EmExecucao` (pagamento aprovado), sem execução iniciada |
   | `EmExecucao` (5) | Status `EmExecucao`, com a execução iniciada |
   | `Finalizada` (6) | Execução finalizada aqui, mesmo depois da entrega |
   | `ForaDaProducao` (7) | OS excluída, ou nos status 5 a 8 sem ter sido finalizada aqui (por exemplo, orçamento recusado ou OS cancelada) |

   Iniciar a execução não muda o status, porque a OS já está `EmExecucao` desde o pagamento: é controle interno da oficina, só com a data e o log `execucao.iniciada`. Não existe o evento `ExecucaoIniciada`, porque nenhum outro serviço o usaria.

6. **`/fila` para a produção, `/ordenadas` para o acompanhamento**:
   - `GET /api/execucoes/fila` (aqui) é a visão da oficina: só as etapas `EmExecucao`, `AguardandoExecucao`, `EmDiagnostico` e `AguardandoDiagnostico`, nessa ordem e, dentro de cada etapa, a mais antiga primeiro (FIFO), pela entrada na fila de execução ou, nas etapas de diagnóstico, pela chegada da OS a este serviço. A OS que aguarda aprovação fica de fora, porque depende do cliente, não da oficina.
   - `GET /api/ordens-servico/ordenadas` (OS Service) continua sendo o acompanhamento do ciclo de vida das OS em aberto, por status e antiguidade.

7. **Publicar só para quem consome** (regra da ADR-020 reescrita):
   - Cada serviço publica nas filas dos serviços que consomem algum evento dele, e não mais nas filas de todos os outros. Este serviço publica só na fila do OS Service (`SqsSettings__FilasPublicacao__0`). A Management e o OS Service publicam também na fila nova (`SqsSettings__FilasPublicacao__2`). O Billing não publica nela, porque nenhum evento dele interessa a este serviço.
   - O `SQS/infra` do MANAGEMENT cria a fila `techchallenge-oficina-execution.fifo` (DLQ `techchallenge-oficina-execution-dlq.fifo`, `maxReceiveCount` 5) e o usuário IAM `techchallenge-oficina-execution-sqs`, que consome a própria fila e só publica na do OS Service. A chave dele fica no SSM, em `/techchallenge-oficina/sqs/execution/...`.
   - Eventos publicados aqui, com `origem` = `execution` e só o `ordemServicoId`: `DiagnosticoIniciado` (OS `Recebida` → `EmDiagnostico`) e `ExecucaoFinalizada` (OS `EmExecucao` → `Finalizada`).

8. **Registros que ficam (*tombstones*) e concorrência**:
   - A exclusão da OS (`OrdemServicoExcluida`) não apaga o item: grava `RemovidaEm`, e a OS sai da produção (`ForaDaProducao`). Se a exclusão é o primeiro evento da OS que chega aqui, grava só esse registro. Os status finais (5 a 8) também são gravados, mesmo de uma OS desconhecida. Assim, um `OrdemServicoValidada` ou um `OrdemServicoStatusAlterado` atrasado não coloca de volta na fila uma OS que já saiu dela.
   - A consulta de um item removido responde 200, com a etapa `ForaDaProducao`; as ações sobre ele respondem 400.
   - Concorrência otimista pela coluna `xmin`: se uma rota e o consumidor da fila alteram a mesma OS ao mesmo tempo, quem grava por último falha. Na rota, a resposta é 500, e a ação pode ser repetida; no consumidor, a mensagem volta para a fila e é processada de novo.

9. **Logs**: catálogo próprio ([ExecucaoLogEventos.cs](../../server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.Execution.UseCases/Features/Execucoes/Logging/ExecucaoLogEventos.cs)), com os mesmos `os.falha`, `integracao.falha` e `evento.*` dos outros serviços e os eventos da produção: `execucao.diagnostico_iniciado` (6001), `execucao.iniciada` (6002, com o tempo na fila) e `execucao.finalizada` (6003, com o tempo de execução). Este serviço não emite `os.status_alterado`: a métrica `oficina.os.tempo_no_status` do Datadog sai desse evento, que o OS Service emite ao mudar o status, e a mesma mudança contaria duas vezes ([observabilidade.md](../observabilidade.md)).

10. **Infraestrutura e pipelines próprios**, como na ADR-019 e na ADR-021:
    - namespace `execution` no mesmo EKS, com o Deployment `execution-api` (Service `NodePort` 30083, atrás da rota `/execution` do API Gateway) e um HPA;
    - imagem `techchallenge-oficina-execution` no ECR (repositório criado manualmente);
    - workflows Bootstrap, Deploy, Destroy e K8s Apply ([.github/workflows/README.md](../../.github/workflows/README.md));
    - a rede, o EKS, o API Gateway, o Datadog e as filas SQS continuam no MANAGEMENT;
    - **HPA de 1 a 2 pods nas quatro APIs:** 4 APIs × 2 + 10 pods de sistema = 18, dentro dos 22 pods que cabem nos 2 nós `t3.small` (ADR-016). Com 3 por API, seriam 22 já em regime, sem folga para o rollout;
    - Bootstrap do OS-SERVICE antes do deste: o OS Service precisa consumir os eventos novos antes de este serviço publicá-los.

11. **Os mesmos padrões de código da Management** (ADR-001 a ADR-014, no repo MANAGEMENT), com os projetos `TechChallenge.Oficina.Execution.*`.

**Consequências**:
- ✅ O trabalho da oficina evolui, escala e é publicado sem depender do OS Service. Com o OS Service fora do ar, as ações daqui são gravadas, e os eventos esperam na fila.
- ✅ O status tem um dono só (o OS Service), e os oito status continuam os mesmos: a Management só passou a publicar também na fila nova, e o código do Billing não mudou.
- ✅ A oficina ganha uma fila de produção e o tempo de cada passo (na fila e de execução), nos logs `execucao.*`.
- ⚠ **Consistência eventual**: logo depois da abertura, a OS ainda não existe aqui (404) até o `OrdemServicoValidada` chegar. A cópia do status fica alguns segundos atrás: a resposta de `iniciar-diagnostico` ainda traz `Recebida`, e `iniciar` responde 400 até chegar o `OrdemServicoStatusAlterado` com `EmExecucao`.
- ⚠ **Publicação sem outbox**: se a publicação falhar, a rota responde 500, a data fica gravada, e a OS continua no status anterior até a ação ser repetida (item 3).
- ⚠ **Indicador de execução do OS Service**: o tempo médio de execução dele vai de `EmExecucao` a `Finalizada` e, por isso, inclui a espera na fila desde o pagamento. O tempo na fila e o tempo de execução de fato ficam só nos logs `execucao.iniciada` e `execucao.finalizada`.
- ⚠ **Conflito de concorrência responde 500**, e não 409 (item 8).
- ⚠ **Os registros de OS excluídas ficam no banco** (*tombstones*), sem limpeza.
- ⚠ **Ordem entre os repositórios**: um OS Service que ainda não consome os eventos novos os ignoraria e apagaria da fila, e a OS não mudaria de status. Por isso, o Bootstrap do OS-SERVICE vem antes do deste.
- ⚠ **Mais peças para operar**: outro RDS (cobrado por hora), outro repositório ECR, outro conjunto de pipelines e secrets, e a ordem entre os repositórios (Destroy deste antes do MANAGEMENT).
- ⚠ **Cluster mais apertado**: no máximo 2 pods por API.
- ⚠ **Monitores de Kubernetes do Datadog** (CPU, memória, restarts e réplicas) continuam restritos ao namespace `oficina`; este serviço aparece no APM, nos logs, no `http_check` e no synthetic ([observabilidade.md](../observabilidade.md)).

**Alternativas Rejeitadas**:
- Manter o diagnóstico e a execução no OS Service:
  - rejeitada porque juntaria o trabalho da oficina (fila, etapas e datas) ao ciclo de vida da OS, no mesmo deploy e no mesmo banco. A fase 4 separa os serviços, cada um com banco e deploy próprios.
- Status novos na OS (como `AguardandoExecucao`), ou um evento `ExecucaoIniciada` levando a OS para `EmExecucao` (4):
  - rejeitada porque mudaria o contrato dos status, compartilhado por número entre os quatro serviços, os e-mails de status da Management e a passagem 3 → 4 do pagamento. As etapas internas dão o detalhe da oficina sem mexer no contrato.
- Consumir o `PagamentoAprovado` direto do Billing para colocar a OS na fila:
  - rejeitada porque a mesma passagem teria duas fontes (o Billing e o OS Service), sem ordem garantida entre elas, e o Billing passaria a publicar em mais uma fila. Pelo `OrdemServicoStatusAlterado`, a fila segue o status do OS Service, que é o dono dele.
- Orquestração (este serviço, ou o AWS Step Functions, coordenando o diagnóstico, o orçamento, o pagamento e a execução):
  - rejeitada pelos motivos da [RFC-004](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-004-Comunicacao-entre-Microsservicos.md): o coordenador precisaria conhecer os passos dos outros serviços. A saga continua coreografada.
- Publicar nas filas de todos os outros serviços (a regra anterior da ADR-020):
  - rejeitada porque mandaria para cá os eventos do Billing, que este serviço não usa, e para a Management e o Billing os eventos daqui, que só o OS Service usa. Com a regra nova, a policy de cada usuário IAM também libera só as filas necessárias.

**Fora do escopo** (evoluções):
- mecânico responsável e atribuição da OS a um mecânico;
- pausa e retrabalho (voltar da execução para o diagnóstico);
- baixa de peças e insumos na produção: o estoque continua sendo debitado pela Management no pagamento aprovado ([ADR-012](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-012-Controle-de-Estoque-de-Insumos.md));
- indicadores da produção (tempo médio na fila e de execução) em rota ou métrica: hoje, só nos logs;
- *transactional outbox*;
- resposta 409 no conflito de concorrência.

**Relacionados**: [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) (repo OS-SERVICE), [ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md) (repo BILLING), [ADR-012](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-012-Controle-de-Estoque-de-Insumos.md), [ADR-014](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-014-Observabilidade-com-Datadog.md), [ADR-016](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-016-Escalabilidade-com-HPA.md), [ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e [RFC-004](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-004-Comunicacao-entre-Microsservicos.md), no repo MANAGEMENT.
