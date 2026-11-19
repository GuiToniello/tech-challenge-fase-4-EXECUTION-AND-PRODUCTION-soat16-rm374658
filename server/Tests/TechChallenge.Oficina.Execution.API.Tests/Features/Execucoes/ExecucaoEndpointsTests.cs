using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Execution.Adapters.Features.Execucoes;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Commands;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Queries;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Execution.API.Tests.Features.Execucoes;

// Controller e adapter reais (o que os endpoints chamam), com os casos de uso simulados
public sealed class ExecucaoEndpointsTests
{
    private readonly Mock<IExecucaoUseCases> _execucaoUseCasesMock = new();

    private ExecucoesController CriarController()
        => new(_execucaoUseCasesMock.Object, new ExecucoesAdapter(), NullLogger<ExecucoesController>.Instance);

    [Fact]
    public async Task MapExecucaoEndpoints_Fila_DeveRetornarOkComAFila()
    {
        IReadOnlyCollection<ExecucaoViewModel> fila = [new ExecucaoViewModel { OrdemServicoId = Guid.NewGuid(), Etapa = 5, EtapaDescricao = "Em execução" }];
        _execucaoUseCasesMock.Setup(u => u.ListarFilaAsync(It.IsAny<ListarFilaExecucaoQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(fila);

        var resultado = await CriarController().GetFila(CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<ExecucaoViewModel>>>(resultado);
        Assert.Same(fila, ok.Value);
    }

    [Fact]
    public async Task MapExecucaoEndpoints_IniciarDiagnostico_DeveRetornarOkComAExecucao()
    {
        var ordemServicoId = Guid.NewGuid();
        var execucao = new ExecucaoViewModel { OrdemServicoId = ordemServicoId, Etapa = 2, EtapaDescricao = "Em diagnóstico" };
        _execucaoUseCasesMock
            .Setup(u => u.IniciarDiagnosticoAsync(It.Is<IniciarDiagnosticoCommand>(c => c.OrdemServicoId == ordemServicoId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execucao);

        var resultado = await CriarController().IniciarDiagnostico(ordemServicoId, CancellationToken.None);

        var ok = Assert.IsType<Ok<ExecucaoViewModel>>(resultado);
        Assert.Same(execucao, ok.Value);
    }

    [Fact]
    public async Task MapExecucaoEndpoints_Iniciar_DeveRetornarBadRequest_QuandoRegraDeNegocioViolada()
    {
        _execucaoUseCasesMock
            .Setup(u => u.IniciarExecucaoAsync(It.IsAny<IniciarExecucaoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Somente ordem de servico em execucao (pagamento aprovado) pode ter a execucao iniciada."));

        var resultado = await CriarController().Iniciar(Guid.NewGuid(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(resultado);
        Assert.Equal("Somente ordem de servico em execucao (pagamento aprovado) pode ter a execucao iniciada.", badRequest.Value!["message"]);
    }

    [Fact]
    public async Task MapExecucaoEndpoints_Finalizar_DeveRetornarNotFound_QuandoOrdemDesconhecida()
    {
        _execucaoUseCasesMock
            .Setup(u => u.FinalizarAsync(It.IsAny<FinalizarExecucaoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Execucao da ordem de servico nao encontrada."));

        var resultado = await CriarController().Finalizar(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFound<Dictionary<string, string?>>>(resultado);
    }
}
