# Observabilidade (Execução e Produção)

O Execução e Produção segue o mesmo contrato de logs da Management API, do OS Service e do Billing. A fonte da verdade (formato do JSON, chaves, regras, valores permitidos e o mapa evento → métrica → dashboard) é o [docs/observabilidade.md do repo MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/observabilidade.md). O Datadog (Agent, métricas de log, monitores, synthetics e dashboard) é criado pelo Terraform do MANAGEMENT, em `kubernetes/infra/addons` ([DATADOG.md](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/kubernetes/infra/DATADOG.md)): este repositório não tem nada do Datadog para configurar.

Aqui ficam só as particularidades deste serviço. **Ao mudar um nome ou valor de evento**, mude também o catálogo da Management, os documentos e, se a mudança afetar métricas, monitores ou o dashboard, o Terraform do MANAGEMENT.

## 1. Serviço

| Projeto | `service` | Namespace | Prefixo no API Gateway |
|---|---|---|---|
| `TechChallenge.Oficina.Execution.API` | `execution-api` | `execution` | `/execution` |

- As tags `service` e `env` (`study`) vêm das labels `tags.datadoghq.com/service` e `tags.datadoghq.com/env` do [Deployment](../kubernetes/k8s/features/execution-api/deployment.yml).
- O Datadog Agent coleta o stdout dos pods. A Single Step Instrumentation injeta o tracer .NET (v3) nos pods do namespace `execution`, pelo admission controller, sem mudança no código nem no Dockerfile.
- O Serilog é configurado em [LoggingExtension.cs](../server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Execution.API/Extensions/LoggingExtension.cs), igual ao da Management: 1 JSON por linha no stdout, com `timestamp`, `level`, `message`, `evento`, `logger.name`, `dd.*`, `error.*` e as propriedades dos eventos. As requisições a `/health*` não geram log.

## 2. Eventos emitidos

O catálogo fica em [ExecucaoLogEventos.cs](../server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.Execution.UseCases/Features/Execucoes/Logging/ExecucaoLogEventos.cs). `os.falha`, `integracao.falha` e `evento.*` têm os mesmos nomes, `EventId` e propriedades dos outros serviços; os eventos da produção (`execucao.*`, 6001 a 6003) são novos.

| Evento (`EventId`) | Nível | Onde é emitido | Propriedades |
|---|---|---|---|
| `execucao.diagnostico_iniciado` (6001) | Information | `ExecucaoUseCases.IniciarDiagnosticoAsync`, depois de gravar e antes de publicar `DiagnosticoIniciado`. Sai também na repetição, que publica o evento de novo | `OrdemServicoId`, `Repeticao` (`true` quando o diagnóstico já tinha sido iniciado e o OS Service ainda não confirmou) |
| `execucao.iniciada` (6002) | Information | `ExecucaoUseCases.IniciarExecucaoAsync`, depois de gravar. Não publica evento | `OrdemServicoId`, `TempoNaFilaSegundos` (número: da entrada na fila de execução, com o pagamento aprovado, até o início) |
| `execucao.finalizada` (6003) | Information | `ExecucaoUseCases.FinalizarAsync`, depois de gravar e antes de publicar `ExecucaoFinalizada`. Sai também na repetição | `OrdemServicoId`, `TempoExecucaoSegundos` (número: do início ao fim da execução), `Repeticao` |
| `os.falha` (2001) | Warning | `ExecucoesController`, quando `iniciar-diagnostico`, `iniciar` ou `finalizar` é rejeitada por regra de negócio (400) ou não encontra a OS (404). Registra só o motivo, sem a exceção | `Etapa`, `OrdemServicoId`, `TipoFalha`, `Motivo` |
| `integracao.falha` (3001) | Error | `EventosPublisher`, se o envio do evento à fila do OS Service falhar (`Operacao` = `publicacao`): registra e relança, e a rota responde 500 com a alteração já gravada (repetir a ação publica de novo). `SqsConsumidorHostedService`, se falhar ao receber mensagens ou ao processar uma delas, inclusive por dados inválidos ou conflito de concorrência (`Operacao` = `consumo`): a mensagem volta para a fila, e as seguintes do lote também, para manter a ordem por OS. Ao obter a URL da fila de consumo, sai como `Warning`, com nova tentativa a cada 5 s | `Integracao` = `sqs`, `Operacao`, `error.*` e, conforme o caso, `Tipo`, `OrdemServicoId`, `EventoId`, `MensagemId` e `Fila` |
| `evento.publicado` (4001) | Information | `EventosPublisher`, depois de enviar o evento à fila do OS Service, a única fila de destino | `Tipo`, `EventoId`, `Fila`, `OrdemServicoId` |
| `evento.consumido` (4002) | Information | `SqsConsumidorHostedService`, evento da Management ou do OS Service processado | `Tipo`, `EventoId`, `Fila` |
| `evento.ignorado` (4003) | Warning | `SqsConsumidorHostedService`: status repetido, exclusão repetida ou tipo que este serviço não trata (`OrdemServicoAberta`, `OrdemServicoAtualizada` e `OrdemServicoRejeitada`, que também chegam aqui). A mensagem é apagada. No status e na exclusão repetidos, um log `Information` sem `EventId` explica o motivo antes | `Tipo`, `EventoId`, `Fila` |
| `http.erro_nao_tratado` (9001) | Error | [GlobalExceptionHandlerMiddleware](../server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Execution.API/Middleware/GlobalExceptionHandlerMiddleware.cs): o 500 da falha de publicação e do conflito de concorrência entre uma rota e o consumidor (`DbUpdateConcurrencyException`), entre outros | `RequestMethod`, `RouteTemplate`, `Integracao` = `postgres` só com o banco indisponível, `error.*` |
| `app.migracao_falhou` (9002) | Critical (`Fatal` no JSON) | [Program.cs](../server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Execution.API/Program.cs), em volta de `ApplyMigrations()`. A API encerra com código 1 | `error.*` |

Logs sem `EventId` que ajudam no diagnóstico (todos `Information`): o motivo do `evento.ignorado` no status repetido ("ja esta no status ...") e na exclusão repetida ("ja saiu da producao"), e o início e o fim do consumidor da fila.

Este serviço não emite `os.criada`, `os.status_alterado` nem `os.encerrada`: o status da OS é do OS Service. O `os.status_alterado` alimenta a métrica `oficina.os.tempo_no_status` (tempo médio por status, no dashboard); se saísse também daqui, a mesma mudança contaria duas vezes. As mudanças de status provocadas por este serviço aparecem no `os.status_alterado` do `os-service-api`, com as etapas `diagnostico` e `finalizacao`.

## 3. Etapas, integrações e operações deste serviço

| `Etapa` | Origem |
|---|---|
| `diagnostico` | `POST /api/execucoes/{ordemServicoId}/iniciar-diagnostico` |
| `execucao` | `POST /api/execucoes/{ordemServicoId}/iniciar` |
| `finalizacao` | `POST /api/execucoes/{ordemServicoId}/finalizar` |

- Aqui, as etapas aparecem só no `os.falha`. Os valores seguem a tabela de etapas do catálogo do MANAGEMENT, e a métrica `oficina.os.falhas` agrupa as falhas por etapa, somando os quatro serviços.
- `TipoFalha`: `regra_negocio` (resposta 400) e `nao_encontrado` (resposta 404).
- `Integracao`: `sqs` (fila) e `postgres` (banco indisponível). Não há `resend` nem `mercadopago`: este serviço não chama APIs externas.
- `Operacao`: `publicacao` e `consumo` (SQS).

## 4. Health checks

| Endpoint | Verifica o banco? | Quem usa |
|---|---|---|
| `/health` | Não | Probes do Kubernetes, NLB (target group `techchallenge-oficina-execution`), `HEALTHCHECK` do Dockerfile e o synthetic `GET /execution/health` pelo API Gateway |
| `/health/ready` | Sim (check `postgres`, tag `ready`): `Healthy` (200) ou `Unhealthy` (503) | Só o `http_check` do Datadog Agent, na instância `execution-api` (`http://execution-api.execution.svc.cluster.local/health/ready`, service check `http.can_connect`) |

Os dois são anônimos e ficam em [HealthExtension.cs](../server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Execution.API/Extensions/HealthExtension.cs). O `readinessProbe` fica em `/health`, para uma queda do banco não tirar os pods do Service. Nenhum dos dois verifica o SQS: as falhas da fila aparecem no `integracao.falha`.

## 5. O que o Datadog cobre deste serviço

- **APM (R1 e R4):** latência p95 e 5xx por rota, por `service`, nos mesmos monitores da Management.
- **Disponibilidade (R3):** `http.can_connect` do `execution-api` e o synthetic `GET /execution/health`, que entra no SLO de uptime.
- **Métricas de log (R7):** `oficina.os.falhas` (por `etapa` e `tipo_falha`) e `oficina.integracao.erros` (por `integracao`) somam os eventos dos quatro serviços; o monitor de erros de integração também alerta para este serviço. Os eventos `execucao.*` e `evento.*` não geram métricas: são consultados no Log Explorer.
- **Limitação:** os monitores e os widgets de Kubernetes do dashboard compartilhado (CPU, memória, CrashLoopBackOff, restarts e réplicas indisponíveis) filtram só o namespace `oficina` (`kube_namespace:oficina`). Os pods deste serviço, no namespace `execution`, ficam de fora deles.

## 6. Consultas úteis

No Log Explorer do Datadog:

| Para ver | Consulta |
|---|---|
| Logs deste serviço | `service:execution-api` |
| Erros (inclui o `Fatal`, que vira `emergency`) | `service:execution-api status:(error OR emergency)` |
| Ações da produção | `@evento:execucao.* service:execution-api` |
| Diagnósticos iniciados | `@evento:execucao.diagnostico_iniciado` |
| Tempo na fila até o início da execução | `@evento:execucao.iniciada` (atributo `@TempoNaFilaSegundos`) |
| Execuções finalizadas e o tempo de execução | `@evento:execucao.finalizada @Repeticao:false` (atributo `@TempoExecucaoSegundos`) |
| Ações repetidas para publicar o evento de novo | `@evento:(execucao.diagnostico_iniciado OR execucao.finalizada) @Repeticao:true` |
| Ações rejeitadas (400 e 404) | `@evento:os.falha service:execution-api` |
| Eventos publicados para o OS Service | `@evento:evento.publicado service:execution-api` |
| Eventos ignorados | `@evento:evento.ignorado service:execution-api` |
| Falhas no SQS | `@Integracao:sqs service:execution-api` |
| Histórico de uma OS nos quatro serviços | `@OrdemServicoId:<guid>` |

Localmente, filtre o stdout do serviço `execution-api` do `docker-compose.yml`:

- Linux/macOS: `docker compose logs execution-api | grep '"evento":"execucao.finalizada"'`
- Windows PowerShell: `docker compose logs execution-api | Select-String '"evento":"execucao.finalizada"'`
