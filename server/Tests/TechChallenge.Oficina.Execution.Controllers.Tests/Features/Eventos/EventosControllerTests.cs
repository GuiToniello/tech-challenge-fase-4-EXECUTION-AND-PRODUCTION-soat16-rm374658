using System.Text.Json;
using Moq;
using TechChallenge.Oficina.Execution.Controllers.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Execution.Controllers.Tests.Features.Eventos;

public sealed class EventosControllerTests
{
    private readonly Mock<IExecucaoEventosUseCases> _useCasesMock = new();
    private readonly Guid _ordemServicoId = Guid.NewGuid();

    private EventosController CriarController() => new(_useCasesMock.Object);

    [Fact]
    public async Task ProcessarAsync_DeveDespacharOrdemServicoValidada_ComSnapshotEmCamelCase()
    {
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();
        var servicoId = Guid.NewGuid();
        _useCasesMock.Setup(useCases => useCases.ProcessarValidadaAsync(It.IsAny<OrdemServicoValidada>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var resultado = await CriarController().ProcessarAsync(
            "OrdemServicoValidada",
            $$"""{"ordemServicoId":"{{_ordemServicoId}}","clienteId":"{{clienteId}}","veiculoId":"{{veiculoId}}","clienteEmail":"cliente@teste.com","estoqueSuficiente":true,"servicos":[{"servicoId":"{{servicoId}}","nome":"Troca de oleo","valor":89.90}],"valorTotal":89.90}""",
            CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _useCasesMock.Verify(useCases => useCases.ProcessarValidadaAsync(
            It.Is<OrdemServicoValidada>(evento =>
                evento.OrdemServicoId == _ordemServicoId &&
                evento.ClienteId == clienteId &&
                evento.VeiculoId == veiculoId &&
                evento.Servicos.Single().ServicoId == servicoId &&
                evento.Servicos.Single().Nome == "Troca de oleo"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarAsync_DeveDespacharOrdemServicoStatusAlterado_ComStatusNumericos()
    {
        var clienteId = Guid.NewGuid();
        _useCasesMock.Setup(useCases => useCases.ProcessarStatusAlteradoAsync(It.IsAny<OrdemServicoStatusAlterado>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var resultado = await CriarController().ProcessarAsync(
            "OrdemServicoStatusAlterado",
            $$"""{"ordemServicoId":"{{_ordemServicoId}}","clienteId":"{{clienteId}}","statusAnterior":3,"statusNovo":4}""",
            CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _useCasesMock.Verify(useCases => useCases.ProcessarStatusAlteradoAsync(
            new OrdemServicoStatusAlterado(_ordemServicoId, clienteId, 3, 4),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarAsync_DeveDespacharOrdemServicoExcluida()
    {
        _useCasesMock.Setup(useCases => useCases.ProcessarExcluidaAsync(It.IsAny<OrdemServicoExcluida>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Ignorado);

        var resultado = await CriarController().ProcessarAsync("OrdemServicoExcluida", $$"""{"ordemServicoId":"{{_ordemServicoId}}"}""", CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        _useCasesMock.Verify(useCases => useCases.ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("DiagnosticoIniciado")]
    [InlineData("ExecucaoFinalizada")]
    [InlineData("OrdemServicoAberta")]
    [InlineData("OrdemServicoRejeitada")]
    [InlineData("OrcamentoGerado")]
    [InlineData("PagamentoAprovado")]
    [InlineData("EventoDesconhecido")]
    [InlineData("")]
    public async Task ProcessarAsync_DeveIgnorarSemChamarUseCases_QuandoTipoNaoTratado(string tipo)
    {
        var resultado = await CriarController().ProcessarAsync(tipo, $$"""{"ordemServicoId":"{{_ordemServicoId}}"}""", CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.Empty(_useCasesMock.Invocations);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"ordemServicoId":"nao-e-guid"}""")]
    [InlineData("nao e json")]
    public async Task ProcessarAsync_DeveLancarJsonException_QuandoDadosInvalidos(string dadosJson)
    {
        var action = async () => await CriarController().ProcessarAsync("OrdemServicoExcluida", dadosJson, CancellationToken.None);

        await Assert.ThrowsAsync<JsonException>(action);
        Assert.Empty(_useCasesMock.Invocations);
    }

    [Fact]
    public async Task ProcessarAsync_DevePropagarExcecaoDoUseCase()
    {
        _useCasesMock.Setup(useCases => useCases.ProcessarExcluidaAsync(It.IsAny<OrdemServicoExcluida>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Banco indisponivel"));

        var action = async () => await CriarController().ProcessarAsync("OrdemServicoExcluida", $$"""{"ordemServicoId":"{{_ordemServicoId}}"}""", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
    }
}
