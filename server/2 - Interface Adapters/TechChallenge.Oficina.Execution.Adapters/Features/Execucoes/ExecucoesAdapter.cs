using Microsoft.AspNetCore.Http;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

namespace TechChallenge.Oficina.Execution.Adapters.Features.Execucoes
{
    public class ExecucoesAdapter : IExecucoesAdapter
    {
        public object Adapt(ExecucoesResult<ExecucaoViewModel, Exception> result)
        {
            if (result.Value != null)
            {
                return TypedResults.Ok(result.Value);
            }

            return CriaErro(result.Error!);
        }

        public object Adapt(ExecucoesResult<IReadOnlyCollection<ExecucaoViewModel>, Exception> result)
        {
            if (result.Value != null)
            {
                return TypedResults.Ok(result.Value);
            }

            return CriaErro(result.Error!);
        }

        public object Adapt(ExecucoesResult<IReadOnlyCollection<EventoHistoricoExecucaoViewModel>, Exception> result)
        {
            if (result.Value != null)
            {
                return TypedResults.Ok(result.Value);
            }

            return CriaErro(result.Error!);
        }

        public object CriaErro(Exception ex)
        {
            if (ex is DomainException)
            {
                return TypedResults.BadRequest(new Dictionary<string, string?> { ["message"] = ex.Message });
            }

            if (ex is KeyNotFoundException)
            {
                return TypedResults.NotFound(new Dictionary<string, string?> { ["message"] = ex.Message });
            }

            return TypedResults.Problem(ex?.Message);
        }
    }
}
