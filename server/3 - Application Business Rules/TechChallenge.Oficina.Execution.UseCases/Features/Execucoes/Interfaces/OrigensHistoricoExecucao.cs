namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;

// Fluxo que gerou o evento da linha do tempo (atributo Origem do item no DynamoDB): acao do mecanico (HTTP) ou evento consumido da fila
public static class OrigensHistoricoExecucao
{
    public const string Acao = "acao";
    public const string Evento = "evento";
}
