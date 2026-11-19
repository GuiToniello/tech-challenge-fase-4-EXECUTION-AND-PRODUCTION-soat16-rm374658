using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Commands;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Queries;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Execution.Controllers.Tests.Features.Execucoes;

public sealed class ExecucoesControllerTests
{
    private readonly Mock<IExecucaoUseCases> _useCasesMock = new();
    private readonly Mock<IExecucoesAdapter> _adapterMock = new();

    private ExecucoesController CriarController(ILogger<ExecucoesController>? logger = null)
    {
        return new ExecucoesController(_useCasesMock.Object, _adapterMock.Object, logger ?? NullLogger<ExecucoesController>.Instance);
    }

    private static (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades) ObterRegistroDeFalha(Mock<ILogger<ExecucoesController>> loggerMock)
    {
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.OsFalha.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades);
    }

    [Fact]
    public async Task GetFila_DeveRepassarQueryERetornarAdaptado()
    {
        var controller = CriarController();
        var adaptado = new object();
        IReadOnlyCollection<ExecucaoViewModel> fila = [new ExecucaoViewModel { OrdemServicoId = Guid.NewGuid(), Etapa = 5 }];
        _useCasesMock.Setup(u => u.ListarFilaAsync(It.IsAny<ListarFilaExecucaoQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(fila);
        _adapterMock.Setup(a => a.Adapt(It.Is<ExecucoesResult<IReadOnlyCollection<ExecucaoViewModel>, Exception>>(r => r.Value == fila))).Returns(adaptado);

        var resposta = await controller.GetFila(CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task GetById_DeveRepassarQueryERetornarAdaptado()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.ObterPorOrdemServicoIdAsync(It.Is<ObterExecucaoPorOrdemServicoIdQuery>(q => q.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecucaoViewModel { OrdemServicoId = id });
        _adapterMock
            .Setup(a => a.Adapt(It.Is<ExecucoesResult<ExecucaoViewModel, Exception>>(r => r.Value!.OrdemServicoId == id)))
            .Returns(adaptado);

        var resposta = await controller.GetById(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task GetById_DeveAdaptarErroSemRegistrarFalha_QuandoKeyNotFoundException()
    {
        var loggerMock = new Mock<ILogger<ExecucoesController>>();
        var controller = CriarController(loggerMock.Object);
        var excecao = new KeyNotFoundException("Execucao da ordem de servico nao encontrada.");
        _useCasesMock
            .Setup(u => u.ObterPorOrdemServicoIdAsync(It.IsAny<ObterExecucaoPorOrdemServicoIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(excecao);

        await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        _adapterMock.Verify(a => a.Adapt(It.Is<ExecucoesResult<ExecucaoViewModel, Exception>>(r => r.Error == excecao)), Times.Once);
        Assert.Empty(loggerMock.Invocations);
    }

    [Fact]
    public async Task IniciarDiagnostico_DeveRepassarComandoComIdERetornarAdaptado()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.IniciarDiagnosticoAsync(It.Is<IniciarDiagnosticoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecucaoViewModel { OrdemServicoId = id, Etapa = 2 });
        _adapterMock.Setup(a => a.Adapt(It.Is<ExecucoesResult<ExecucaoViewModel, Exception>>(r => r.Value!.OrdemServicoId == id))).Returns(adaptado);

        var resposta = await controller.IniciarDiagnostico(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task IniciarDiagnostico_DeveRegistrarFalhaDeRegraDeNegocio_QuandoDomainException()
    {
        var loggerMock = new Mock<ILogger<ExecucoesController>>();
        var controller = CriarController(loggerMock.Object);
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.IniciarDiagnosticoAsync(It.IsAny<IniciarDiagnosticoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Somente ordem de servico recebida pode ter o diagnostico iniciado."));

        await controller.IniciarDiagnostico(id, CancellationToken.None);

        var (nivel, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal("diagnostico", propriedades["Etapa"]);
        Assert.Equal(id, propriedades["OrdemServicoId"]);
        Assert.Equal("regra_negocio", propriedades["TipoFalha"]);
        Assert.Equal("Somente ordem de servico recebida pode ter o diagnostico iniciado.", propriedades["Motivo"]);
        _adapterMock.Verify(a => a.Adapt(It.Is<ExecucoesResult<ExecucaoViewModel, Exception>>(r => r.Error is DomainException)), Times.Once);
    }

    [Fact]
    public async Task IniciarDiagnostico_DeveRegistrarFalhaDeNaoEncontrado_QuandoKeyNotFoundException()
    {
        var loggerMock = new Mock<ILogger<ExecucoesController>>();
        var controller = CriarController(loggerMock.Object);
        _useCasesMock
            .Setup(u => u.IniciarDiagnosticoAsync(It.IsAny<IniciarDiagnosticoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Execucao da ordem de servico nao encontrada."));

        await controller.IniciarDiagnostico(Guid.NewGuid(), CancellationToken.None);

        var (_, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal("diagnostico", propriedades["Etapa"]);
        Assert.Equal("nao_encontrado", propriedades["TipoFalha"]);
        _adapterMock.Verify(a => a.Adapt(It.Is<ExecucoesResult<ExecucaoViewModel, Exception>>(r => r.Error is KeyNotFoundException)), Times.Once);
    }

    [Fact]
    public async Task Iniciar_DeveRepassarComandoComIdERetornarAdaptado()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.IniciarExecucaoAsync(It.Is<IniciarExecucaoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecucaoViewModel { OrdemServicoId = id, Etapa = 5 });
        _adapterMock.Setup(a => a.Adapt(It.IsAny<ExecucoesResult<ExecucaoViewModel, Exception>>())).Returns(adaptado);

        var resposta = await controller.Iniciar(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
        _useCasesMock.Verify(u => u.IniciarExecucaoAsync(It.Is<IniciarExecucaoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Iniciar_DeveRegistrarFalhaNaEtapaExecucao_QuandoDomainException()
    {
        var loggerMock = new Mock<ILogger<ExecucoesController>>();
        var controller = CriarController(loggerMock.Object);
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.IniciarExecucaoAsync(It.IsAny<IniciarExecucaoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("A execucao da ordem de servico ja foi iniciada."));

        await controller.Iniciar(id, CancellationToken.None);

        var (nivel, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal("execucao", propriedades["Etapa"]);
        Assert.Equal(id, propriedades["OrdemServicoId"]);
        Assert.Equal("regra_negocio", propriedades["TipoFalha"]);
    }

    [Fact]
    public async Task Finalizar_DeveRepassarComandoComIdERetornarAdaptado()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.FinalizarAsync(It.Is<FinalizarExecucaoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecucaoViewModel { OrdemServicoId = id, Etapa = 6 });
        _adapterMock.Setup(a => a.Adapt(It.IsAny<ExecucoesResult<ExecucaoViewModel, Exception>>())).Returns(adaptado);

        var resposta = await controller.Finalizar(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task Finalizar_DeveRegistrarFalhaNaEtapaFinalizacao_QuandoKeyNotFoundException()
    {
        var loggerMock = new Mock<ILogger<ExecucoesController>>();
        var controller = CriarController(loggerMock.Object);
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.FinalizarAsync(It.IsAny<FinalizarExecucaoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Execucao da ordem de servico nao encontrada."));

        await controller.Finalizar(id, CancellationToken.None);

        var (_, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal("finalizacao", propriedades["Etapa"]);
        Assert.Equal("nao_encontrado", propriedades["TipoFalha"]);
        Assert.Equal("Execucao da ordem de servico nao encontrada.", propriedades["Motivo"]);
    }

    [Fact]
    public async Task Finalizar_DevePropagarFalhaDeIntegracaoSemRegistrarOsFalha()
    {
        var loggerMock = new Mock<ILogger<ExecucoesController>>();
        var controller = CriarController(loggerMock.Object);
        _useCasesMock
            .Setup(u => u.FinalizarAsync(It.IsAny<FinalizarExecucaoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SQS indisponivel"));

        var action = async () => await controller.Finalizar(Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Empty(loggerMock.Invocations);
        Assert.Empty(_adapterMock.Invocations);
    }
}
