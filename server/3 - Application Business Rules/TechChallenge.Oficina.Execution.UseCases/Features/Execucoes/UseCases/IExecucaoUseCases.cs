using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Commands;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Queries;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

public interface IExecucaoUseCases
{
    Task<ExecucaoViewModel> ObterPorOrdemServicoIdAsync(ObterExecucaoPorOrdemServicoIdQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ExecucaoViewModel>> ListarFilaAsync(ListarFilaExecucaoQuery query, CancellationToken cancellationToken = default);
    Task<ExecucaoViewModel> IniciarDiagnosticoAsync(IniciarDiagnosticoCommand command, CancellationToken cancellationToken = default);
    Task<ExecucaoViewModel> IniciarExecucaoAsync(IniciarExecucaoCommand command, CancellationToken cancellationToken = default);
    Task<ExecucaoViewModel> FinalizarAsync(FinalizarExecucaoCommand command, CancellationToken cancellationToken = default);
}
