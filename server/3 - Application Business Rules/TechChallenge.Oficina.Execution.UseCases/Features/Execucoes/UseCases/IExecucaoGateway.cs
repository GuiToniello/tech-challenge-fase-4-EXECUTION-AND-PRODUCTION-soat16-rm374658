using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;

namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

public interface IExecucaoGateway
{
    Task AdicionarAsync(Execucao execucao, CancellationToken cancellationToken = default);
    Task AtualizarAsync(Execucao execucao, CancellationToken cancellationToken = default);
    Task<Execucao?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);

    // Itens da fila de producao, sem ordem definida: OS recebida, em diagnostico ou em execucao, que nao foi finalizada nem excluida
    Task<IReadOnlyCollection<Execucao>> ListarFilaAsync(CancellationToken cancellationToken = default);
}
