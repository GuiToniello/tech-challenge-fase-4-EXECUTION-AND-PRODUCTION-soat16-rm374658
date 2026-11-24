namespace TechChallenge.Oficina.Execution.Controllers.Features.Execucoes
{
    public interface IExecucoesController
    {
        Task<object> GetFila(CancellationToken cancellationToken);

        Task<object> GetById(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> GetHistorico(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> IniciarDiagnostico(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> Iniciar(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> Finalizar(Guid ordemServicoId, CancellationToken cancellationToken);
    }
}
