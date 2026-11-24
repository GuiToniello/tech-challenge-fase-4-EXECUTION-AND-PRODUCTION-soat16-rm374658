namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;

// Porta de saida para a linha do tempo da producao (Amazon DynamoDB no Frameworks & Drivers).
// A linha do tempo e complementar e descartavel: a fonte da verdade continua no PostgreSQL e nenhuma regra le a linha do tempo para decidir algo.
public interface IHistoricoExecucaoGateway
{
    // Best-effort: falha de integracao nao lanca excecao (a implementacao registra integracao.falha e segue)
    Task RegistrarAsync(EventoHistoricoExecucao evento, CancellationToken cancellationToken = default);

    // Eventos da ordem de servico em ordem cronologica; vazio quando nao ha registros
    Task<IReadOnlyCollection<EventoHistoricoExecucao>> ListarAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);
}
