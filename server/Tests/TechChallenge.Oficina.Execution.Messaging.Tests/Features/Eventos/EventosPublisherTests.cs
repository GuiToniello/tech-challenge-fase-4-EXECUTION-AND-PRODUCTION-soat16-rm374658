using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Execution.Messaging.Configuration;
using TechChallenge.Oficina.Execution.Messaging.Features.Eventos;
using TechChallenge.Oficina.Execution.Messaging.Sqs;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using Xunit;

namespace TechChallenge.Oficina.Execution.Messaging.Tests.Features.Eventos;

public sealed class EventosPublisherTests
{
    private const string FilaOsService = "techchallenge-oficina-os-service.fifo";
    private const string UrlFilaOsService = "http://elasticmq:9324/000000000000/techchallenge-oficina-os-service.fifo";

    private readonly Mock<ISqsClient> _sqsClientMock = new();
    private readonly SqsSettings _sqsSettings = new()
    {
        FilaConsumo = "techchallenge-oficina-execution.fifo",
        FilasPublicacao = [FilaOsService]
    };
    private readonly List<(string Url, string Corpo, string Tipo, string Grupo, string Deduplicacao)> _enviadas = [];

    public EventosPublisherTests()
    {
        _sqsClientMock.Setup(c => c.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>())).ReturnsAsync(UrlFilaOsService);
        _sqsClientMock
            .Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((url, corpo, tipo, grupo, deduplicacao, _) => _enviadas.Add((url, corpo, tipo, grupo, deduplicacao)))
            .Returns(Task.CompletedTask);
    }

    private EventosPublisher CriarPublisher(ILogger<EventosPublisher>? logger = null) =>
        new(_sqsClientMock.Object, _sqsSettings, logger ?? NullLogger<EventosPublisher>.Instance);

    private static IReadOnlyDictionary<string, object?> ObterPropriedades(Moq.IInvocation invocacao) =>
        ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

    [Fact]
    public async Task PublicarAsync_DeveEnviarEnvelopeEmCamelCaseParaAFilaDoOsService()
    {
        var ordemServicoId = Guid.NewGuid();

        await CriarPublisher().PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId);

        var (url, corpo, tipo, grupo, deduplicacao) = Assert.Single(_enviadas);
        Assert.Equal(UrlFilaOsService, url);
        Assert.Equal("DiagnosticoIniciado", tipo);

        using var json = JsonDocument.Parse(corpo);
        var envelope = json.RootElement;
        Assert.Equal(["id", "tipo", "versao", "ocorridoEm", "origem", "dados"], envelope.EnumerateObject().Select(propriedade => propriedade.Name));
        Assert.Equal("DiagnosticoIniciado", envelope.GetProperty("tipo").GetString());
        Assert.Equal(1, envelope.GetProperty("versao").GetInt32());
        Assert.Equal("execution", envelope.GetProperty("origem").GetString());

        var ocorridoEm = envelope.GetProperty("ocorridoEm").GetString()!;
        Assert.EndsWith("Z", ocorridoEm);
        Assert.InRange(DateTime.Parse(ocorridoEm, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        var dados = envelope.GetProperty("dados");
        Assert.Equal(["ordemServicoId"], dados.EnumerateObject().Select(propriedade => propriedade.Name));
        Assert.Equal(ordemServicoId, dados.GetProperty("ordemServicoId").GetGuid());

        // Grupo FIFO = ordem de servico; deduplicacao = id do envelope
        Assert.Equal(ordemServicoId.ToString("D"), grupo);
        Assert.Equal(envelope.GetProperty("id").GetGuid().ToString("D"), deduplicacao);
    }

    [Fact]
    public async Task PublicarAsync_DeveSerializarExecucaoFinalizadaComOTipoDoEvento()
    {
        var ordemServicoId = Guid.NewGuid();

        await CriarPublisher().PublicarAsync(new ExecucaoFinalizada(ordemServicoId), ordemServicoId);

        var enviada = Assert.Single(_enviadas);
        Assert.Equal("ExecucaoFinalizada", enviada.Tipo);
        using var json = JsonDocument.Parse(enviada.Corpo);
        Assert.Equal("ExecucaoFinalizada", json.RootElement.GetProperty("tipo").GetString());
        Assert.Equal(ordemServicoId, json.RootElement.GetProperty("dados").GetProperty("ordemServicoId").GetGuid());
    }

    [Fact]
    public async Task PublicarAsync_DeveEnviarOMesmoEnvelopeParaCadaFilaConfigurada()
    {
        const string outraFila = "techchallenge-oficina-management.fifo";
        const string urlOutraFila = "http://elasticmq:9324/000000000000/techchallenge-oficina-management.fifo";
        _sqsSettings.FilasPublicacao.Add(outraFila);
        _sqsClientMock.Setup(c => c.GetQueueUrlAsync(outraFila, It.IsAny<CancellationToken>())).ReturnsAsync(urlOutraFila);
        var ordemServicoId = Guid.NewGuid();

        await CriarPublisher().PublicarAsync(new ExecucaoFinalizada(ordemServicoId), ordemServicoId);

        Assert.Equal([UrlFilaOsService, urlOutraFila], _enviadas.Select(enviada => enviada.Url));
        Assert.Single(_enviadas.Select(enviada => enviada.Corpo).Distinct());
        Assert.Single(_enviadas.Select(enviada => enviada.Deduplicacao).Distinct());
    }

    [Fact]
    public async Task PublicarAsync_DeveGerarIdDeEnvelopeDiferentePorPublicacao()
    {
        var publisher = CriarPublisher();
        var ordemServicoId = Guid.NewGuid();

        // Repeticao do diagnostico: o evento sai de novo com outro id (nao e descartado pela deduplicacao do SQS)
        await publisher.PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId);
        await publisher.PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId);

        Assert.Equal(2, _enviadas.Count);
        Assert.Equal(2, _enviadas.Select(enviada => enviada.Deduplicacao).Distinct().Count());
        Assert.All(_enviadas, enviada => Assert.Equal(ordemServicoId.ToString("D"), enviada.Grupo));
    }

    [Fact]
    public async Task PublicarAsync_DeveObterUrlDaFilaUmaVez()
    {
        var publisher = CriarPublisher();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId);
        await publisher.PublicarAsync(new ExecucaoFinalizada(ordemServicoId), ordemServicoId);

        _sqsClientMock.Verify(c => c.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarEventoPublicado()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var ordemServicoId = Guid.NewGuid();

        await CriarPublisher(loggerMock.Object).PublicarAsync(new ExecucaoFinalizada(ordemServicoId), ordemServicoId);

        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.EventoPublicado.Id);
        var propriedades = ObterPropriedades(invocacao);
        Assert.Equal(LogLevel.Information, (LogLevel)invocacao.Arguments[0]);
        Assert.Equal(FilaOsService, propriedades["Fila"]);
        Assert.Equal("ExecucaoFinalizada", propriedades["Tipo"]);
        Assert.Equal(ordemServicoId, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoEnvioFalhar()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var falha = new InvalidOperationException("SQS indisponivel");
        var ordemServicoId = Guid.NewGuid();
        _sqsClientMock
            .Setup(c => c.SendAsync(UrlFilaOsService, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await CriarPublisher(loggerMock.Object).PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(falha, exception);
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.IntegracaoFalha.Id);
        var propriedades = ObterPropriedades(invocacao);
        Assert.Equal(LogLevel.Error, (LogLevel)invocacao.Arguments[0]);
        Assert.Same(falha, invocacao.Arguments[3]);
        Assert.Equal("sqs", propriedades["Integracao"]);
        Assert.Equal("publicacao", propriedades["Operacao"]);
        Assert.Equal("DiagnosticoIniciado", propriedades["Tipo"]);
        Assert.Equal(ordemServicoId, propriedades["OrdemServicoId"]);
        Assert.Equal(FilaOsService, propriedades["Fila"]);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.EventoPublicado.Id);
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarFalhaDeIntegracao_QuandoFilaNaoEncontrada()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        _sqsClientMock
            .Setup(c => c.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fila inexistente"));
        var ordemServicoId = Guid.NewGuid();

        var action = async () => await CriarPublisher(loggerMock.Object).PublicarAsync(new ExecucaoFinalizada(ordemServicoId), ordemServicoId);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.IntegracaoFalha.Id);
        Assert.Empty(_enviadas);
    }

    [Fact]
    public async Task PublicarAsync_DeveRelancarSemRegistrarFalha_QuandoCancelado()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _sqsClientMock
            .Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));
        var ordemServicoId = Guid.NewGuid();

        var action = async () => await CriarPublisher(loggerMock.Object).PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId, cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.IntegracaoFalha.Id);
    }
}
