# ADR-025: Linha do Tempo da Produção no Amazon DynamoDB

**Status**: Aceita (fase 4). Complementa a [ADR-023](ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md), em que o PostgreSQL continua sendo o banco do Execução e Produção, e segue o padrão do histórico de pagamentos do Billing ([ADR-024](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-024-Historico-de-Pagamentos-no-DynamoDB.md))

**Resumo**:
O Execução e Produção passa a guardar uma **linha do tempo da produção** de cada OS (diagnóstico iniciado, entrada na fila de execução, início e fim da execução) numa tabela do **Amazon DynamoDB**, um banco NoSQL. O PostgreSQL continua sendo a fonte da verdade: o item da produção, a cópia do status, as etapas, as datas e todas as regras não mudam. A linha do tempo é complementar e descartável: a gravação é *best-effort*, os itens expiram em 180 dias, e nenhuma regra a lê para decidir algo. A consulta é feita em `GET /api/execucoes/{ordemServicoId}/historico`.

> As ADRs e RFCs são numeradas em sequência única entre os repositórios [MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658), [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), [BILLING](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658), EXECUTION (este) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice das decisões está no [docs/arquitetura.md do MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- A tabela `execucoes` do RDS (ADR-023) guarda um item por OS, só com a data da primeira vez de cada passo (`diagnostico_iniciado_em`, `entrou_na_fila_em`, `execucao_iniciada_em` e `finalizada_em`). As repetições de `iniciar-diagnostico` e `finalizar` (que publicam o evento de novo) e o tempo na fila e de execução ficam só nos logs `execucao.*`, que a API não consulta.
- Esse registro tem o formato típico de NoSQL, como o histórico de pagamentos do Billing (ADR-024): só recebe gravações novas (*append-only*), é lido sempre pela OS (uma chave), sem junções, os atributos variam conforme o passo (o tempo na fila só no início da execução, o tempo de execução só na finalização) e pode expirar sozinho.
- A [RFC-002](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-002-Escolha-do-Banco-de-Dados.md) rejeitou o DynamoDB como banco principal, porque o modelo da oficina é cheio de relacionamentos ([ADR-017](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md)). Isso continua valendo: aqui, ele entra só para um dado sem relacionamentos.
- Um banco por serviço ([ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e ADR-023): a tabela é só do Execução e Produção, separada da tabela do Billing.
- É um projeto de estudo: o custo precisa ficar perto de zero, e o ambiente é destruído depois da demonstração.

**Decisão**:

1. **Papel de cada banco**:

   | Banco | O que guarda | Papel |
   |---|---|---|
   | PostgreSQL (RDS, banco `oficina_execution`) | O item da produção de cada OS (`execucoes`): dados da OS, cópia do status e a data de cada passo | **Fonte da verdade**. A fila, as etapas e todas as regras saem dele, como antes |
   | DynamoDB (tabela `techchallenge-oficina-execution-producao-historico`) | Um item por passo da produção | **Linha do tempo complementar e descartável**, só para consulta e auditoria. Nenhuma regra a lê; se faltar um item, nada quebra |

2. **Tabela**, em [DB/infra/dynamodb.tf](../../DB/infra/dynamodb.tf), no mesmo Terraform, state e pipeline do RDS:
   - sob demanda (`PAY_PER_REQUEST`), sem capacidade provisionada;
   - chave de partição `OrdemServicoId` (S) e chave de ordenação `OcorridoEm` (S): data e hora UTC em ISO-8601 com milissegundos, mais `#` e 8 caracteres hexadecimais (por exemplo, `2026-11-24T10:00:00.000Z#abcd1234`). A data ordena os passos, e o sufixo evita colisão entre dois registros no mesmo milissegundo;
   - TTL no atributo `ExpiraEm` (epoch em segundos, preenchido pela API: data do passo + 180 dias);
   - sem proteção contra exclusão (o Destroy apaga a tabela), sem PITR e sem streams;
   - outputs `dynamodb_historico_producao_nome` e `dynamodb_historico_producao_arn`.

3. **Item**, sem dados pessoais (da OS, só o id):

   | Atributo | Conteúdo |
   |---|---|
   | `Tipo` | `DiagnosticoIniciado`, `EntrouNaFila`, `ExecucaoIniciada` ou `ExecucaoFinalizada` |
   | `Origem` | `acao` (rota do mecânico) ou `evento` (evento consumido da fila SQS) |
   | `Repeticao` | Booleano: `true` quando `iniciar-diagnostico` ou `finalizar` foi repetido antes da confirmação do OS Service (o evento foi publicado de novo) |
   | `TempoNaFilaSegundos` | Só no `ExecucaoIniciada` |
   | `TempoExecucaoSegundos` | Só no `ExecucaoFinalizada` |

   Os tempos são os mesmos valores dos logs `execucao.iniciada` e `execucao.finalizada`. O `OcorridoEm` é o instante da ação: na primeira vez, a mesma data gravada no PostgreSQL; na repetição, o instante do novo pedido.

4. **Quando registra**: sempre depois de gravar no PostgreSQL, de registrar o log e de publicar o evento (quando há evento), com `CancellationToken.None`, para o registro não se perder se a requisição for cancelada. Se a publicação falhar, a rota responde 500 e nada é registrado; ao repetir a ação, a repetição é registrada.

   | Fluxo | O que registra |
   |---|---|
   | `iniciar-diagnostico` (`ExecucaoUseCases.IniciarDiagnosticoAsync`) | `DiagnosticoIniciado` (`acao`), também a cada repetição (`Repeticao` = `true`) |
   | `iniciar` (`ExecucaoUseCases.IniciarExecucaoAsync`) | `ExecucaoIniciada` (`acao`), com o tempo na fila |
   | `finalizar` (`ExecucaoUseCases.FinalizarAsync`) | `ExecucaoFinalizada` (`acao`), com o tempo de execução, também a cada repetição (`Repeticao` = `true`) |
   | `OrdemServicoStatusAlterado` (`ExecucaoEventosUseCases.ProcessarStatusAlteradoAsync`) | `EntrouNaFila` (`evento`), só quando a OS entra na fila de execução pela primeira vez (`EntrouNaFilaEm` passa de vazio a preenchido). O status repetido é ignorado antes de qualquer gravação, e uma nova passagem por `EmExecucao` mantém a data da primeira entrada, então não registra de novo |

   As ações rejeitadas (400 e 404) não registram nada.

5. **Best-effort**:
   - `RegistrarAsync` nunca lança: uma falha (DynamoDB fora do ar, permissão negada, tabela inexistente) gera `integracao.falha` (3001) como **Warning**, com `Integracao` = `dynamodb` e `Operacao` = `registro_historico` (mais `OrdemServicoId` e `Tipo`), e o fluxo segue: a ação responde 200, e o consumidor apaga a mensagem. Só aquela linha falta na linha do tempo.
   - A gravação é síncrona: com o DynamoDB fora do ar, a ação ou o evento só termina depois do *timeout* e das novas tentativas do SDK.
   - `ListarAsync` registra `integracao.falha` como **Error** (`Operacao` = `consulta_historico`) e relança a falha: a rota de consulta responde 500, pelo middleware global ([ADR-013](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md)).
   - A consulta é um `Query` na partição da OS, em ordem crescente da chave de ordenação, com leitura consistente (`ConsistentRead`) e percorrendo todas as páginas.

6. **Rota**: `GET /api/execucoes/{ordemServicoId}/historico`, com JWT (na AWS, `/execution/api/execucoes/{ordemServicoId}/historico`, pela rota `/execution/{proxy+}` que já existe). Responde 200 com a lista em ordem cronológica, ou com a lista vazia quando não há registros. A rota não consulta o PostgreSQL: uma OS que este serviço não conhece também responde 200, com a lista vazia.

7. **Código**, no padrão de porta e adapter do SQS (ADR-023) e do histórico de pagamentos do Billing (ADR-024):
   - porta `IHistoricoExecucaoGateway`, record `EventoHistoricoExecucao` e as constantes `TiposHistoricoExecucao` e `OrigensHistoricoExecucao` no projeto UseCases (`Features/Execucoes/Interfaces`), com o `EventoHistoricoExecucaoViewModel` e o mapeamento no `ExecucaoProfile`. A porta chega ao `ExecucaoUseCases` e ao `ExecucaoEventosUseCases` pelo construtor;
   - projeto novo `TechChallenge.Oficina.Execution.NoSql` (anel "1 - Frameworks & Drivers"), o único com o pacote `AWSSDK.DynamoDBv2` (4.0.*): `HistoricoExecucaoGateway`, a interface interna `IDynamoDbClient` com o `DynamoDbClientAdapter` (o único ponto que usa o SDK) e o `AddInfraNoSql`;
   - configuração `DynamoDbSettings`: `TabelaHistoricoExecucoes` (na AWS, `DynamoDbSettings__TabelaHistoricoExecucoes` no ConfigMap) e `ServiceUrl` (só localmente);
   - testes no projeto `TechChallenge.Oficina.Execution.NoSql.Tests`, com o SDK mockado; os testes de integração usam uma linha do tempo em memória.

8. **Credenciais e IAM, sem usuário nem chave novos**: o [DB/infra/iam.tf](../../DB/infra/iam.tf) anexa a política inline `dynamodb-historico-producao` (só `dynamodb:PutItem` e `dynamodb:Query`, no ARN da tabela) ao usuário IAM do Execução e Produção que já existe, `techchallenge-oficina-execution-sqs`, criado pelo `SQS/infra` do MANAGEMENT. A chave desse usuário já chega ao pod (SSM → Secret `execution-api-secrets`) e passa a servir às filas e à tabela. Na AWS, a API nunca cria nem descreve a tabela (sem `CreateTable` e `DescribeTable`): ela é do Terraform. A tabela e a política do Billing ficam no repo BILLING, em outro usuário: nenhum dos dois serviços acessa a tabela do outro. Se o MANAGEMENT recriar o usuário, a política some junto, e é preciso rodar o **Bootstrap** deste repositório de novo (o Deploy só reaplica o `DB/infra` quando os arquivos dele mudam).

9. **Ambiente local**: o `docker-compose.yml` sobe o DynamoDB Local (`amazon/dynamodb-local:3.1.0`, em memória, container `oficina-execution-dynamodb`), na porta `8001` do host (`DYNAMODB_PORT`), porque o do Billing usa a `8000`. A API aponta para ele por `DynamoDbSettings__ServiceUrl`, com as mesmas credenciais fictícias do ElasticMQ. Só com `ServiceUrl` preenchido a API cria a tabela no startup (`GarantirTabelasLocaisAsync`), com as mesmas chaves e o mesmo TTL; uma falha aí só gera um aviso, e a API sobe.

**Consequências**:
- ✅ Dá para ver o caminho de cada OS na oficina (diagnóstico, entrada na fila, início e fim da execução, com os tempos e as repetições) por uma rota, sem mudar a tabela `execucoes` nem as regras da produção.
- ✅ Custo perto de zero: tabela sob demanda, poucos itens e expiração automática, sem instância ligada.
- ✅ Uma falha no DynamoDB não derruba a produção: as ações e o consumo dos eventos seguem.
- ✅ O mesmo padrão do Billing (ADR-024): NoSQL onde o formato combina (gravação e leitura por chave) e relacional onde há relacionamentos, como recomenda a RFC-002.
- ⚠ **A linha do tempo pode ter buracos**: uma falha de gravação perde aquela linha (fica só o log `integracao.falha`), uma falha na publicação do evento não registra nada até a ação ser repetida, e os itens somem depois de 180 dias. As datas que valem continuam sendo as da tabela `execucoes`.
- ⚠ **Sem atomicidade entre os dois bancos**: as gravações são separadas. Se o processo cair entre elas, a linha do tempo fica sem o registro.
- ⚠ **Mais latência com o DynamoDB fora do ar**: como a gravação é síncrona, as ações e o consumo esperam o *timeout* e as novas tentativas do SDK antes de seguir.
- ⚠ **O monitor de erros de integração também alerta para `dynamodb`**: a métrica `oficina.integracao.erros` (filtro `@Integracao:*`, no MANAGEMENT) conta inclusive o Warning do registro ([observabilidade.md](../observabilidade.md)).
- ⚠ **Mais uma ligação com o MANAGEMENT**: a política fica no usuário IAM criado por ele; se o usuário for recriado, o Bootstrap deste repositório precisa rodar de novo. O usuário `terraform` da CI precisa de permissões no DynamoDB e de `iam:PutUserPolicy`, `iam:GetUserPolicy` e `iam:DeleteUserPolicy` (hoje ele é administrador).
- ⚠ **Mais uma peça local**: o DynamoDB Local roda em memória, e a linha do tempo some quando o container reinicia.

**Alternativas Rejeitadas**:
- Levar o item da produção (o agregado `Execucao`) para o DynamoDB e tirar o RDS:
  - rejeitada porque mexeria em 45 a 50 arquivos, trocaria o controle de concorrência pela coluna `xmin` por gravações condicionais, exigiria um `Scan` ou um índice secundário (GSI) para montar a fila e colocaria o DynamoDB no caminho crítico de todas as rotas e do consumidor SQS. A linha do tempo mostra o uso de NoSQL sem esse risco.
- Guardar no DynamoDB apontamentos do mecânico (registros lançados por ele durante o diagnóstico e a execução):
  - rejeitada porque exigiria rotas e regras novas, fora do escopo da ADR-023, que deixou o mecânico responsável e a pausa como evoluções. A linha do tempo registra só o que o serviço já faz.
- Uma raiz de Terraform e um state só para a tabela:
  - rejeitada porque traria novos jobs nas pipelines e mais um check obrigatório. A tabela fica no `DB/infra`, no mesmo state e pipeline do RDS.
- Um usuário IAM e uma chave só para o DynamoDB:
  - rejeitada porque traria novos parâmetros no SSM, novas chaves no Secret e credenciais explícitas no código, sem ganho real: a política inline já limita o acesso a `PutItem` e `Query` nesta tabela.

**Fora do escopo**: linha do tempo do `OrdemServicoValidada` e da exclusão da OS (`OrdemServicoExcluida`); levar o item da produção para o NoSQL; PITR (backup contínuo); DynamoDB Streams; métricas e monitores específicos do DynamoDB no Datadog.

**Relacionados**: [ADR-023](ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md) (este repo), [ADR-024](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-024-Historico-de-Pagamentos-no-DynamoDB.md) (repo BILLING), [ADR-004](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-004-Gateways-e-Persistencia.md), [ADR-013](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md), [ADR-014](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-014-Observabilidade-com-Datadog.md), [ADR-017](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md), [ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e [RFC-002](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-002-Escolha-do-Banco-de-Dados.md), no repo MANAGEMENT.
