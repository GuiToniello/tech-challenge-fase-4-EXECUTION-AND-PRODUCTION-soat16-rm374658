using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Commands;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Queries;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

namespace TechChallenge.Oficina.Execution.Controllers.Features.Execucoes
{
    public class ExecucoesController : IExecucoesController
    {
        private readonly IExecucaoUseCases _execucaoUseCases;
        private readonly IExecucoesAdapter _execucoesAdapter;
        private readonly ILogger<ExecucoesController> _logger;

        public ExecucoesController(
            IExecucaoUseCases execucaoUseCases,
            IExecucoesAdapter execucoesAdapter,
            ILogger<ExecucoesController> logger)
        {
            _execucaoUseCases = execucaoUseCases;
            _execucoesAdapter = execucoesAdapter;
            _logger = logger;
        }

        public async Task<object> GetFila(CancellationToken cancellationToken)
        {
            var fila = await _execucaoUseCases.ListarFilaAsync(new ListarFilaExecucaoQuery(), cancellationToken);
            var result = ExecucoesResult.From(fila);

            return _execucoesAdapter.Adapt(result);
        }

        public async Task<object> GetById(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var query = new ObterExecucaoPorOrdemServicoIdQuery { OrdemServicoId = ordemServicoId };
                var execucao = await _execucaoUseCases.ObterPorOrdemServicoIdAsync(query, cancellationToken);
                var result = ExecucoesResult.From(execucao);

                return _execucoesAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                var result = ExecucoesResult.FromError<ExecucaoViewModel>(exception);
                return _execucoesAdapter.Adapt(result);
            }
        }

        public async Task<object> IniciarDiagnostico(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var execucao = await _execucaoUseCases.IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = ExecucoesResult.From(execucao);

                return _execucoesAdapter.Adapt(result);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(ExecucaoLogEventos.Etapas.Diagnostico, ordemServicoId, exception);
                var result = ExecucoesResult.FromError<ExecucaoViewModel>(exception);
                return _execucoesAdapter.Adapt(result);
            }
        }

        public async Task<object> Iniciar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var execucao = await _execucaoUseCases.IniciarExecucaoAsync(new IniciarExecucaoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = ExecucoesResult.From(execucao);

                return _execucoesAdapter.Adapt(result);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(ExecucaoLogEventos.Etapas.Execucao, ordemServicoId, exception);
                var result = ExecucoesResult.FromError<ExecucaoViewModel>(exception);
                return _execucoesAdapter.Adapt(result);
            }
        }

        public async Task<object> Finalizar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var execucao = await _execucaoUseCases.FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = ExecucoesResult.From(execucao);

                return _execucoesAdapter.Adapt(result);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(ExecucaoLogEventos.Etapas.Finalizacao, ordemServicoId, exception);
                var result = ExecucoesResult.FromError<ExecucaoViewModel>(exception);
                return _execucoesAdapter.Adapt(result);
            }
        }

        private void RegistrarFalha(string etapa, Guid ordemServicoId, Exception exception)
        {
            var tipoFalha = exception is KeyNotFoundException
                ? ExecucaoLogEventos.TiposFalha.NaoEncontrado
                : ExecucaoLogEventos.TiposFalha.RegraNegocio;

            // Rejeicao de regra de negocio nao e erro de aplicacao: registra so o motivo, sem stack trace
            _logger.LogWarning(
                ExecucaoLogEventos.OsFalha,
                "Falha na etapa {Etapa} da ordem de servico {OrdemServicoId} ({TipoFalha}): {Motivo}",
                etapa,
                ordemServicoId,
                tipoFalha,
                exception.Message);
        }
    }
}
