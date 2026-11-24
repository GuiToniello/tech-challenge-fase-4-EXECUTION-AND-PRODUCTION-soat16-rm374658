using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

namespace TechChallenge.Oficina.Execution.API.Features.Execucoes
{
    public static class ExecucaoEndpoints
    {
        public static RouteGroupBuilder MapExecucaoEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes
                .MapGroup("/api/execucoes")
                .WithTags("Execucoes");

            // Fila de producao: em execucao, aguardando execucao, em diagnostico e aguardando diagnostico; dentro da etapa, a mais antiga primeiro
            group.MapGet(
                "/fila",
                (
                    IExecucoesController execucoesController,
                    CancellationToken cancellationToken
                ) => execucoesController.GetFila(cancellationToken))
                .Produces<IReadOnlyCollection<ExecucaoViewModel>>(StatusCodes.Status200OK);

            group.MapGet(
                "/{ordemServicoId:guid}",
                (
                    IExecucoesController execucoesController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => execucoesController.GetById(ordemServicoId, cancellationToken))
                .WithName("GetExecucaoByOrdemServicoId")
                .Produces<ExecucaoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound);

            // Linha do tempo da producao da OS (DynamoDB), em ordem cronologica; lista vazia quando nao ha registros
            group.MapGet(
                "/{ordemServicoId:guid}/historico",
                (
                    IExecucoesController execucoesController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => execucoesController.GetHistorico(ordemServicoId, cancellationToken))
                .Produces<IReadOnlyCollection<EventoHistoricoExecucaoViewModel>>(StatusCodes.Status200OK);

            // Publica DiagnosticoIniciado: o OS Service passa a OS para em diagnostico. Repetida antes da confirmacao, publica de novo
            group.MapPost(
                "/{ordemServicoId:guid}/iniciar-diagnostico",
                (
                    IExecucoesController execucoesController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => execucoesController.IniciarDiagnostico(ordemServicoId, cancellationToken))
                .Produces<ExecucaoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            // Controle interno da oficina (sem evento): so com a OS em execucao, ou seja, com o pagamento aprovado
            group.MapPost(
                "/{ordemServicoId:guid}/iniciar",
                (
                    IExecucoesController execucoesController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => execucoesController.Iniciar(ordemServicoId, cancellationToken))
                .Produces<ExecucaoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            // Publica ExecucaoFinalizada: o OS Service passa a OS para finalizada. Repetida antes da confirmacao, publica de novo
            group.MapPost(
                "/{ordemServicoId:guid}/finalizar",
                (
                    IExecucoesController execucoesController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => execucoesController.Finalizar(ordemServicoId, cancellationToken))
                .Produces<ExecucaoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            return group;
        }
    }
}
