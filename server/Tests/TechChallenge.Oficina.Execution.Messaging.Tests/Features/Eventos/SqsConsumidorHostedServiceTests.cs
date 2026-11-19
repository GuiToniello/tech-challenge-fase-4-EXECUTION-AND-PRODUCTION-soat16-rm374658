using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TechChallenge.Oficina.Execution.Controllers.Features.Eventos;
using TechChallenge.Oficina.Execution.Messaging.Configuration;
using TechChallenge.Oficina.Execution.Messaging.Features.Eventos;
using TechChallenge.Oficina.Execution.Messaging.Sqs;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using Xunit;

namespace TechChallenge.Oficina.Execution.Messaging.Tests.Features.Eventos;

public sealed class SqsConsumidorHostedServiceTests : IDisposable
{
    private const string FilaConsumo = "techchallenge-oficina-execution.fifo";
    private const string UrlFila = "http://elasticmq:9324/000000000000/techchallenge-oficina-execution.fifo";

    private readonly Mock<ISqsClient> _sqsClientMock = new();
    private readonly Mock<IEventosController> _eventosControllerMock = new();
    private readonly Mock<ILogger<SqsConsumidorHostedService>> _loggerMock = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly Guid _eventoId = Guid.NewGuid();
    private readonly Guid _ordemServicoId = Guid.NewGuid();

    public SqsConsumidorHostedServiceTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _eventosControllerMock.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SqsConsumidorHostedService CriarConsumidor() =>
        new(_sqsClientMock.Object, _serviceProvider.GetRequiredService<IServiceScopeFactory>(), new SqsSettings { FilaConsumo = FilaConsumo }, _loggerMock.Object);

    private MensagemSqs CriarMensagem(string tipo = "OrdemServicoExcluida", string mensagemId = "mensagem-1", string recibo = "recibo-1") =>
        new(mensagemId, recibo, $$$"""{"id":"{{{_eventoId}}}","tipo":"{{{tipo}}}","versao":1,"ocorridoEm":"2026-11-19T12:00:00.0000000Z","origem":"os-service","dados":{"ordemServicoId":"{{{_ordemServicoId}}}"}}""");

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeLog(EventId evento)
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    private void VerificarMensagemNaoExcluida() =>
        _sqsClientMock.Verify(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task ProcessarMensagemAsync_DeveEntregarTipoEDadosAoControllerEExcluir_QuandoProcessado()
    {
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync("OrdemServicoExcluida", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var removida = await CriarConsumidor().ProcessarMensagemAsync(UrlFila, CriarMensagem(), CancellationToken.None);

        Assert.True(removida);
        _eventosControllerMock.Verify(c => c.ProcessarAsync(
            "OrdemServicoExcluida",
            It.Is<string>(dados => dados.Contains($"\"ordemServicoId\":\"{_ordemServicoId}\"")),
            It.IsAny<CancellationToken>()), Times.Once);
        _sqsClientMock.Verify(c => c.DeleteAsync(UrlFila, "recibo-1", It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(ExecucaoLogEventos.EventoConsumido);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal("OrdemServicoExcluida", propriedades["Tipo"]);
        Assert.Equal(_eventoId, propriedades["EventoId"]);
        Assert.Equal(FilaConsumo, propriedades["Fila"]);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_DeveExcluirERegistrarIgnorado_QuandoEventoIgnorado()
    {
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Ignorado);

        var removida = await CriarConsumidor().ProcessarMensagemAsync(UrlFila, CriarMensagem("EventoDesconhecido"), CancellationToken.None);

        Assert.True(removida);
        _sqsClientMock.Verify(c => c.DeleteAsync(UrlFila, "recibo-1", It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(ExecucaoLogEventos.EventoIgnorado);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal("EventoDesconhecido", propriedades["Tipo"]);
        Assert.DoesNotContain(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.EventoConsumido.Id);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_NaoDeveExcluirERegistrarFalhaDeConsumo_QuandoProcessamentoLancar()
    {
        var falha = new InvalidOperationException("Banco indisponivel");
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var removida = await CriarConsumidor().ProcessarMensagemAsync(UrlFila, CriarMensagem(), CancellationToken.None);

        Assert.False(removida);
        VerificarMensagemNaoExcluida();
        var (nivel, propriedades, excecao) = ObterRegistroDeLog(ExecucaoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("sqs", propriedades["Integracao"]);
        Assert.Equal("consumo", propriedades["Operacao"]);
        Assert.Equal("OrdemServicoExcluida", propriedades["Tipo"]);
        Assert.Equal(_eventoId, propriedades["EventoId"]);
        Assert.Equal("mensagem-1", propriedades["MensagemId"]);
    }

    [Theory]
    [InlineData("nao e json")]
    [InlineData("""{"id":"1b0f4c1e-8d4a-4b7e-9c21-5a0e7d9f1b23","versao":1,"dados":{}}""")]
    [InlineData("""{"id":"1b0f4c1e-8d4a-4b7e-9c21-5a0e7d9f1b23","tipo":"OrdemServicoExcluida","versao":1}""")]
    public async Task ProcessarMensagemAsync_NaoDeveExcluirNemProcessar_QuandoMensagemForaDoEnvelope(string corpo)
    {
        var removida = await CriarConsumidor().ProcessarMensagemAsync(UrlFila, new MensagemSqs("mensagem-2", "recibo-2", corpo), CancellationToken.None);

        Assert.False(removida);
        Assert.Empty(_eventosControllerMock.Invocations);
        VerificarMensagemNaoExcluida();
        var (nivel, propriedades, _) = ObterRegistroDeLog(ExecucaoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Equal("consumo", propriedades["Operacao"]);
        Assert.Equal("mensagem-2", propriedades["MensagemId"]);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_NaoDeveExcluirNemRegistrarFalha_QuandoServicoEncerrando()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var removida = await CriarConsumidor().ProcessarMensagemAsync(UrlFila, CriarMensagem(), cancelamento.Token);

        Assert.False(removida);
        VerificarMensagemNaoExcluida();
        Assert.DoesNotContain(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.IntegracaoFalha.Id);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_DeveRegistrarFalha_QuandoExclusaoFalhar()
    {
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);
        _sqsClientMock
            .Setup(c => c.DeleteAsync(UrlFila, "recibo-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SQS indisponivel"));

        var removida = await CriarConsumidor().ProcessarMensagemAsync(UrlFila, CriarMensagem(), CancellationToken.None);

        Assert.False(removida);
        var (nivel, _, _) = ObterRegistroDeLog(ExecucaoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
    }

    [Fact]
    public async Task ExecuteAsync_DeveObterFilaReceberEProcessarMensagensAteParar()
    {
        var mensagemExcluida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recebimentos = 0;

        _sqsClientMock.Setup(c => c.GetQueueUrlAsync(FilaConsumo, It.IsAny<CancellationToken>())).ReturnsAsync(UrlFila);
        _sqsClientMock
            .Setup(c => c.ReceiveAsync(UrlFila, 10, 20, It.IsAny<CancellationToken>()))
            .Returns<string, int, int, CancellationToken>(async (_, _, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref recebimentos) == 1)
                {
                    return [CriarMensagem()];
                }

                // Long polling sem mensagens: espera ate o servico parar
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return [];
            });
        _sqsClientMock
            .Setup(c => c.DeleteAsync(UrlFila, "recibo-1", It.IsAny<CancellationToken>()))
            .Callback(() => mensagemExcluida.TrySetResult())
            .Returns(Task.CompletedTask);
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var consumidor = CriarConsumidor();
        await consumidor.StartAsync(CancellationToken.None);
        await mensagemExcluida.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await consumidor.StopAsync(CancellationToken.None);

        _sqsClientMock.Verify(c => c.GetQueueUrlAsync(FilaConsumo, It.IsAny<CancellationToken>()), Times.Once);
        _eventosControllerMock.Verify(c => c.ProcessarAsync("OrdemServicoExcluida", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && (LogLevel)chamada.Arguments[0] >= LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_DeveInterromperOLote_QuandoUmaMensagemFalhar()
    {
        var segundoRecebimento = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recebimentos = 0;

        _sqsClientMock.Setup(c => c.GetQueueUrlAsync(FilaConsumo, It.IsAny<CancellationToken>())).ReturnsAsync(UrlFila);
        _sqsClientMock
            .Setup(c => c.ReceiveAsync(UrlFila, 10, 20, It.IsAny<CancellationToken>()))
            .Returns<string, int, int, CancellationToken>(async (_, _, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref recebimentos) == 1)
                {
                    // Lote FIFO com dois eventos da mesma OS: o primeiro falha
                    return [CriarMensagem("OrdemServicoStatusAlterado", "mensagem-1", "recibo-1"), CriarMensagem("OrdemServicoExcluida", "mensagem-2", "recibo-2")];
                }

                segundoRecebimento.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return [];
            });
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync("OrdemServicoStatusAlterado", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Banco indisponivel"));
        _eventosControllerMock
            .Setup(c => c.ProcessarAsync("OrdemServicoExcluida", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var consumidor = CriarConsumidor();
        await consumidor.StartAsync(CancellationToken.None);
        await segundoRecebimento.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await consumidor.StopAsync(CancellationToken.None);

        // A mensagem seguinte da mesma OS nao passa na frente da que falhou: volta para a fila com ela
        _eventosControllerMock.Verify(c => c.ProcessarAsync("OrdemServicoExcluida", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerificarMensagemNaoExcluida();
    }

    [Fact]
    public async Task ExecuteAsync_DeveTentarObterAFilaDeNovo_QuandoElasticMqAindaNaoSubiu()
    {
        var filaObtida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tentativas = 0;

        _sqsClientMock
            .Setup(c => c.GetQueueUrlAsync(FilaConsumo, It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, _) => Interlocked.Increment(ref tentativas) == 1
                ? Task.FromException<string>(new InvalidOperationException("ElasticMQ iniciando"))
                : Task.FromResult(UrlFila));
        _sqsClientMock
            .Setup(c => c.ReceiveAsync(UrlFila, 10, 20, It.IsAny<CancellationToken>()))
            .Returns<string, int, int, CancellationToken>(async (_, _, _, cancellationToken) =>
            {
                filaObtida.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return [];
            });

        var consumidor = CriarConsumidor();
        await consumidor.StartAsync(CancellationToken.None);
        await filaObtida.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await consumidor.StopAsync(CancellationToken.None);

        Assert.Equal(2, tentativas);
        var (nivel, propriedades, _) = ObterRegistroDeLog(ExecucaoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal(FilaConsumo, propriedades["Fila"]);
    }
}
