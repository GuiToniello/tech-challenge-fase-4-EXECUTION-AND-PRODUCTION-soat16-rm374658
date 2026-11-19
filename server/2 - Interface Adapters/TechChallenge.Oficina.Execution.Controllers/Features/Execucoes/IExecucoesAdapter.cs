using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

namespace TechChallenge.Oficina.Execution.Controllers.Features.Execucoes
{
    public interface IExecucoesAdapter
    {
        object Adapt(ExecucoesResult<ExecucaoViewModel, Exception> result);

        object Adapt(ExecucoesResult<IReadOnlyCollection<ExecucaoViewModel>, Exception> result);
    }
}
