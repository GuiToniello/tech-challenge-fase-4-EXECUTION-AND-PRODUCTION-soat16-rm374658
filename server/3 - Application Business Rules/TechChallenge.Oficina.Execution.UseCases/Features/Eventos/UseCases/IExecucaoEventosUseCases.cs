using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.UseCases;

public interface IExecucaoEventosUseCases
{
    Task<ResultadoProcessamentoEvento> ProcessarValidadaAsync(OrdemServicoValidada evento, CancellationToken cancellationToken = default);
    Task<ResultadoProcessamentoEvento> ProcessarStatusAlteradoAsync(OrdemServicoStatusAlterado evento, CancellationToken cancellationToken = default);
    Task<ResultadoProcessamentoEvento> ProcessarExcluidaAsync(OrdemServicoExcluida evento, CancellationToken cancellationToken = default);
}
