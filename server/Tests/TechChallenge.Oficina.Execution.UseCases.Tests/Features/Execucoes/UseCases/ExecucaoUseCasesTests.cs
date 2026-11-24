using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Commands;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Mappings;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Queries;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Execution.UseCases.Tests.Features.Execucoes.UseCases;

public sealed class ExecucaoUseCasesTests
{
    private static readonly IMapper Mapper = CriarMapper();
    private static readonly DateTime Inicio = new(2026, 11, 19, 8, 00, 00, DateTimeKind.Utc);

    private readonly Mock<IExecucaoGateway> _gatewayMock = new();
    private readonly Mock<IEventosPublisher> _eventosPublisherMock = new();
    private readonly Mock<IHistoricoExecucaoGateway> _historicoMock = new();
    private readonly Mock<ILogger<ExecucaoUseCases>> _loggerMock = new();

    private static IMapper CriarMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(ExecucaoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IMapper>();
    }

    private ExecucaoUseCases CriarUseCases() => new(Mapper, _gatewayMock.Object, _eventosPublisherMock.Object, _historicoMock.Object, _loggerMock.Object);

    private static Execucao CriarRecebida(DateTime? criadoEm = null)
        => Execucao.CriarPorValidacao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ["Troca de oleo"], criadoEm ?? Inicio);

    private static Execucao CriarNoStatus(StatusOrdemServico status, DateTime? alteradoEm = null)
    {
        var execucao = CriarRecebida();
        execucao.AtualizarStatus(status, alteradoEm ?? Inicio.AddMinutes(1));
        return execucao;
    }

    private static Execucao CriarComExecucaoIniciada(DateTime? entrouNaFilaEm = null)
    {
        var execucao = CriarNoStatus(StatusOrdemServico.EmExecucao, entrouNaFilaEm);
        execucao.IniciarExecucao(Inicio.AddMinutes(5));
        return execucao;
    }

    private Execucao ConfigurarExistente(Execucao execucao)
    {
        _gatewayMock.Setup(g => g.ObterPorIdAsync(execucao.Id, It.IsAny<CancellationToken>())).ReturnsAsync(execucao);
        return execucao;
    }

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades) ObterRegistroDeLog(EventId evento)
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades);
    }

    private List<string> RegistrarPassos()
    {
        var passos = new List<string>();
        _gatewayMock
            .Setup(g => g.AtualizarAsync(It.IsAny<Execucao>(), It.IsAny<CancellationToken>()))
            .Callback(() => passos.Add("gravar"))
            .Returns(Task.CompletedTask);
        _eventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<It.IsAnyType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => passos.Add("publicar"))
            .Returns(Task.CompletedTask);
        _historicoMock
            .Setup(h => h.RegistrarAsync(It.IsAny<EventoHistoricoExecucao>(), It.IsAny<CancellationToken>()))
            .Callback(() => passos.Add("historico"))
            .Returns(Task.CompletedTask);
        return passos;
    }

    // Eventos enviados a linha do tempo, na ordem; o registro e best-effort e nunca usa o token da requisicao
    private List<EventoHistoricoExecucao> ObterEventosDoHistorico()
    {
        var registros = _historicoMock.Invocations
            .Where(chamada => chamada.Method.Name == nameof(IHistoricoExecucaoGateway.RegistrarAsync))
            .ToList();

        Assert.All(registros, chamada => Assert.Equal(CancellationToken.None, (CancellationToken)chamada.Arguments[1]));
        return registros.Select(chamada => (EventoHistoricoExecucao)chamada.Arguments[0]).ToList();
    }

    private static void VerificarEvento(EventoHistoricoExecucao evento, Guid ordemServicoId, string tipo, bool repeticao)
    {
        Assert.Equal(ordemServicoId, evento.OrdemServicoId);
        Assert.Equal(tipo, evento.Tipo);
        Assert.Equal(OrigensHistoricoExecucao.Acao, evento.Origem);
        Assert.Equal(repeticao, evento.Repeticao);
        Assert.Equal(DateTimeKind.Utc, evento.OcorridoEm.Kind);
    }

    private void VerificarSemGravacao() =>
        _gatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Execucao>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveRetornarViewModelComStatusEEtapa()
    {
        var execucao = ConfigurarExistente(CriarRecebida());

        var resultado = await CriarUseCases().ObterPorOrdemServicoIdAsync(new ObterExecucaoPorOrdemServicoIdQuery { OrdemServicoId = execucao.Id });

        Assert.Equal(execucao.Id, resultado.OrdemServicoId);
        Assert.Equal(1, resultado.StatusOrdemServico);
        Assert.Equal("Recebida", resultado.StatusOrdemServicoDescricao);
        Assert.Equal(1, resultado.Etapa);
        Assert.Equal("Aguardando diagnóstico", resultado.EtapaDescricao);
        Assert.Equal(["Troca de oleo"], resultado.Servicos);
    }

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveLancarKeyNotFound_QuandoNaoExiste()
    {
        var action = async () => await CriarUseCases().ObterPorOrdemServicoIdAsync(new ObterExecucaoPorOrdemServicoIdQuery { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Execucao da ordem de servico nao encontrada.", exception.Message);
    }

    [Fact]
    public async Task ListarFilaAsync_DeveOrdenarPorEtapaEDentroDelaPelaMaisAntiga()
    {
        var aguardandoDiagnosticoNova = CriarRecebida(Inicio.AddMinutes(10));
        var aguardandoDiagnosticoAntiga = CriarRecebida(Inicio);
        var emDiagnosticoConfirmado = Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmDiagnostico, Inicio.AddMinutes(8));
        var emDiagnosticoPedido = CriarRecebida(Inicio.AddMinutes(4));
        emDiagnosticoPedido.IniciarDiagnostico(Inicio.AddMinutes(20));
        var aguardandoExecucaoNova = CriarNoStatus(StatusOrdemServico.EmExecucao, Inicio.AddHours(3));
        var aguardandoExecucaoAntiga = CriarNoStatus(StatusOrdemServico.EmExecucao, Inicio.AddHours(2));
        var emExecucaoNova = CriarComExecucaoIniciada(Inicio.AddHours(1));
        var emExecucaoAntiga = CriarComExecucaoIniciada(Inicio.AddMinutes(30));
        _gatewayMock
            .Setup(g => g.ListarFilaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                aguardandoDiagnosticoNova, emExecucaoNova, emDiagnosticoConfirmado, aguardandoExecucaoAntiga,
                aguardandoDiagnosticoAntiga, emDiagnosticoPedido, aguardandoExecucaoNova, emExecucaoAntiga
            ]);

        var fila = await CriarUseCases().ListarFilaAsync(new ListarFilaExecucaoQuery());

        Assert.Equal(
            new[]
            {
                emExecucaoAntiga.Id, emExecucaoNova.Id,
                aguardandoExecucaoAntiga.Id, aguardandoExecucaoNova.Id,
                emDiagnosticoPedido.Id, emDiagnosticoConfirmado.Id,
                aguardandoDiagnosticoAntiga.Id, aguardandoDiagnosticoNova.Id
            },
            fila.Select(item => item.OrdemServicoId));
        Assert.Equal([5, 5, 4, 4, 2, 2, 1, 1], fila.Select(item => item.Etapa));
    }

    [Fact]
    public async Task ListarFilaAsync_DeveDescartarItensForaDasEtapasDaFila()
    {
        var naFila = CriarRecebida();
        var aguardandoAprovacao = CriarNoStatus(StatusOrdemServico.AguardandoAprovacao);
        var finalizada = CriarComExecucaoIniciada();
        finalizada.Finalizar(Inicio.AddHours(1));
        var removida = Execucao.CriarRemovida(Guid.NewGuid(), Inicio);
        _gatewayMock
            .Setup(g => g.ListarFilaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([aguardandoAprovacao, naFila, finalizada, removida]);

        var fila = await CriarUseCases().ListarFilaAsync(new ListarFilaExecucaoQuery());

        Assert.Equal(naFila.Id, Assert.Single(fila).OrdemServicoId);
    }

    [Fact]
    public async Task ListarFilaAsync_DeveRetornarListaVazia_QuandoNaoHaItens()
    {
        _gatewayMock.Setup(g => g.ListarFilaAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var fila = await CriarUseCases().ListarFilaAsync(new ListarFilaExecucaoQuery());

        Assert.Empty(fila);
    }

    [Fact]
    public async Task IniciarDiagnosticoAsync_DeveGravarPublicarERegistrar_NaPrimeiraVez()
    {
        var execucao = ConfigurarExistente(CriarRecebida());
        using var cancelamento = new CancellationTokenSource();

        var resultado = await CriarUseCases().IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = execucao.Id }, cancelamento.Token);

        Assert.Equal(2, resultado.Etapa);
        Assert.Equal(1, resultado.StatusOrdemServico);
        Assert.NotNull(resultado.DiagnosticoIniciadoEm);
        _gatewayMock.Verify(g => g.AtualizarAsync(execucao, cancelamento.Token), Times.Once);
        // Ja gravado: a publicacao nao e cancelada junto com a requisicao
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new DiagnosticoIniciado(execucao.Id), execucao.Id, CancellationToken.None), Times.Once);
        var (nivel, propriedades) = ObterRegistroDeLog(ExecucaoLogEventos.ExecucaoDiagnosticoIniciado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(execucao.Id, propriedades["OrdemServicoId"]);
        Assert.False((bool)propriedades["Repeticao"]!);
    }

    [Fact]
    public async Task IniciarDiagnosticoAsync_DeveGravarPublicarERegistrarNoHistoricoNessaOrdem()
    {
        var execucao = ConfigurarExistente(CriarRecebida());
        var passos = RegistrarPassos();

        await CriarUseCases().IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = execucao.Id });

        Assert.Equal(["gravar", "publicar", "historico"], passos);
    }

    [Fact]
    public async Task IniciarDiagnosticoAsync_DeveRegistrarDiagnosticoIniciadoNoHistorico_NaPrimeiraVez()
    {
        var execucao = ConfigurarExistente(CriarRecebida());
        using var cancelamento = new CancellationTokenSource();

        var resultado = await CriarUseCases().IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = execucao.Id }, cancelamento.Token);

        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, execucao.Id, TiposHistoricoExecucao.DiagnosticoIniciado, repeticao: false);
        // Na primeira vez, o instante da linha do tempo e a mesma data gravada no PostgreSQL
        Assert.Equal(resultado.DiagnosticoIniciadoEm, evento.OcorridoEm);
        Assert.Null(evento.TempoNaFilaSegundos);
        Assert.Null(evento.TempoExecucaoSegundos);
    }

    [Fact]
    public async Task IniciarDiagnosticoAsync_DevePublicarDeNovoSemGravar_QuandoRepetidoAntesDaConfirmacao()
    {
        var execucao = CriarRecebida();
        execucao.IniciarDiagnostico(Inicio.AddMinutes(10));
        ConfigurarExistente(execucao);
        var antes = DateTime.UtcNow;

        var resultado = await CriarUseCases().IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = execucao.Id });

        Assert.Equal(Inicio.AddMinutes(10), resultado.DiagnosticoIniciadoEm);
        VerificarSemGravacao();
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new DiagnosticoIniciado(execucao.Id), execucao.Id, CancellationToken.None), Times.Once);
        var (_, propriedades) = ObterRegistroDeLog(ExecucaoLogEventos.ExecucaoDiagnosticoIniciado);
        Assert.True((bool)propriedades["Repeticao"]!);
        // A repeticao tambem vai para a linha do tempo, no instante do novo pedido (a data do primeiro continua no PostgreSQL)
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, execucao.Id, TiposHistoricoExecucao.DiagnosticoIniciado, repeticao: true);
        Assert.InRange(evento.OcorridoEm, antes, DateTime.UtcNow);
    }

    [Fact]
    public async Task IniciarDiagnosticoAsync_DeveLancarSemGravarNemPublicar_QuandoOrdemNaoEstaRecebida()
    {
        var execucao = ConfigurarExistente(CriarNoStatus(StatusOrdemServico.EmDiagnostico));

        var action = async () => await CriarUseCases().IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = execucao.Id });

        await Assert.ThrowsAsync<DomainException>(action);
        VerificarSemGravacao();
        Assert.Empty(_eventosPublisherMock.Invocations);
        Assert.Empty(_historicoMock.Invocations);
    }

    [Fact]
    public async Task IniciarDiagnosticoAsync_DeveLancarKeyNotFound_QuandoNaoExiste()
    {
        var action = async () => await CriarUseCases().IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = Guid.NewGuid() });

        await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Empty(_eventosPublisherMock.Invocations);
        Assert.Empty(_historicoMock.Invocations);
    }

    [Fact]
    public async Task IniciarDiagnosticoAsync_DevePropagarFalhaDaPublicacaoMantendoOQueFoiGravado()
    {
        var execucao = ConfigurarExistente(CriarRecebida());
        _eventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<DiagnosticoIniciado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SQS indisponivel"));

        var action = async () => await CriarUseCases().IniciarDiagnosticoAsync(new IniciarDiagnosticoCommand { OrdemServicoId = execucao.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        _gatewayMock.Verify(g => g.AtualizarAsync(execucao, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(execucao.DiagnosticoIniciadoEm);
        // Sem publicacao, nada vai para a linha do tempo: a nova tentativa do mecanico (repeticao) publica e registra
        Assert.Empty(_historicoMock.Invocations);
    }

    [Fact]
    public async Task IniciarExecucaoAsync_DeveGravarERegistrarSemPublicarEvento()
    {
        var execucao = ConfigurarExistente(CriarNoStatus(StatusOrdemServico.EmExecucao));

        var resultado = await CriarUseCases().IniciarExecucaoAsync(new IniciarExecucaoCommand { OrdemServicoId = execucao.Id });

        Assert.Equal(5, resultado.Etapa);
        Assert.Equal("Em execução", resultado.EtapaDescricao);
        Assert.Equal(4, resultado.StatusOrdemServico);
        Assert.NotNull(resultado.ExecucaoIniciadaEm);
        _gatewayMock.Verify(g => g.AtualizarAsync(execucao, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_eventosPublisherMock.Invocations);
        var (nivel, propriedades) = ObterRegistroDeLog(ExecucaoLogEventos.ExecucaoIniciada);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(execucao.Id, propriedades["OrdemServicoId"]);
        Assert.True((double)propriedades["TempoNaFilaSegundos"]! > 0);
    }

    [Fact]
    public async Task IniciarExecucaoAsync_DeveRegistrarExecucaoIniciadaNoHistoricoComOTempoNaFilaDoLog()
    {
        var execucao = ConfigurarExistente(CriarNoStatus(StatusOrdemServico.EmExecucao));
        using var cancelamento = new CancellationTokenSource();
        var passos = RegistrarPassos();

        var resultado = await CriarUseCases().IniciarExecucaoAsync(new IniciarExecucaoCommand { OrdemServicoId = execucao.Id }, cancelamento.Token);

        // Sem evento para publicar: a linha do tempo vem logo depois da gravacao
        Assert.Equal(["gravar", "historico"], passos);
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, execucao.Id, TiposHistoricoExecucao.ExecucaoIniciada, repeticao: false);
        Assert.Equal(resultado.ExecucaoIniciadaEm, evento.OcorridoEm);
        var (_, propriedades) = ObterRegistroDeLog(ExecucaoLogEventos.ExecucaoIniciada);
        Assert.Equal((double)propriedades["TempoNaFilaSegundos"]!, evento.TempoNaFilaSegundos);
        Assert.Null(evento.TempoExecucaoSegundos);
    }

    [Fact]
    public async Task IniciarExecucaoAsync_DeveLancarSemGravar_QuandoPagamentoAindaNaoAprovado()
    {
        var execucao = ConfigurarExistente(CriarNoStatus(StatusOrdemServico.AguardandoAprovacao));

        var action = async () => await CriarUseCases().IniciarExecucaoAsync(new IniciarExecucaoCommand { OrdemServicoId = execucao.Id });

        await Assert.ThrowsAsync<DomainException>(action);
        VerificarSemGravacao();
        Assert.Empty(_historicoMock.Invocations);
    }

    [Fact]
    public async Task FinalizarAsync_DeveGravarPublicarERegistrar_NaPrimeiraVez()
    {
        var execucao = ConfigurarExistente(CriarComExecucaoIniciada());
        using var cancelamento = new CancellationTokenSource();

        var resultado = await CriarUseCases().FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = execucao.Id }, cancelamento.Token);

        Assert.Equal(6, resultado.Etapa);
        Assert.Equal("Finalizada", resultado.EtapaDescricao);
        Assert.Equal(4, resultado.StatusOrdemServico);
        Assert.NotNull(resultado.FinalizadaEm);
        _gatewayMock.Verify(g => g.AtualizarAsync(execucao, cancelamento.Token), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new ExecucaoFinalizada(execucao.Id), execucao.Id, CancellationToken.None), Times.Once);
        var (nivel, propriedades) = ObterRegistroDeLog(ExecucaoLogEventos.ExecucaoFinalizada);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(execucao.Id, propriedades["OrdemServicoId"]);
        Assert.False((bool)propriedades["Repeticao"]!);
        Assert.True((double)propriedades["TempoExecucaoSegundos"]! > 0);
    }

    [Fact]
    public async Task FinalizarAsync_DeveGravarPublicarERegistrarNoHistoricoNessaOrdem()
    {
        var execucao = ConfigurarExistente(CriarComExecucaoIniciada());
        var passos = RegistrarPassos();

        await CriarUseCases().FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = execucao.Id });

        Assert.Equal(["gravar", "publicar", "historico"], passos);
    }

    [Fact]
    public async Task FinalizarAsync_DeveRegistrarExecucaoFinalizadaNoHistoricoComOTempoDeExecucaoDoLog()
    {
        var execucao = ConfigurarExistente(CriarComExecucaoIniciada());
        using var cancelamento = new CancellationTokenSource();

        var resultado = await CriarUseCases().FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = execucao.Id }, cancelamento.Token);

        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, execucao.Id, TiposHistoricoExecucao.ExecucaoFinalizada, repeticao: false);
        Assert.Equal(resultado.FinalizadaEm, evento.OcorridoEm);
        var (_, propriedades) = ObterRegistroDeLog(ExecucaoLogEventos.ExecucaoFinalizada);
        Assert.Equal((double)propriedades["TempoExecucaoSegundos"]!, evento.TempoExecucaoSegundos);
        Assert.Null(evento.TempoNaFilaSegundos);
    }

    [Fact]
    public async Task FinalizarAsync_DevePublicarDeNovoSemGravar_QuandoRepetidoAntesDaConfirmacao()
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Finalizar(Inicio.AddHours(2));
        ConfigurarExistente(execucao);
        var passos = RegistrarPassos();
        var antes = DateTime.UtcNow;

        var resultado = await CriarUseCases().FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = execucao.Id });

        Assert.Equal(Inicio.AddHours(2), resultado.FinalizadaEm);
        VerificarSemGravacao();
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new ExecucaoFinalizada(execucao.Id), execucao.Id, CancellationToken.None), Times.Once);
        var (_, propriedades) = ObterRegistroDeLog(ExecucaoLogEventos.ExecucaoFinalizada);
        Assert.True((bool)propriedades["Repeticao"]!);
        // A repeticao vai para a linha do tempo com o mesmo tempo de execucao do log (calculado com a data da primeira finalizacao)
        Assert.Equal(["publicar", "historico"], passos);
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, execucao.Id, TiposHistoricoExecucao.ExecucaoFinalizada, repeticao: true);
        Assert.InRange(evento.OcorridoEm, antes, DateTime.UtcNow);
        Assert.Equal((double)propriedades["TempoExecucaoSegundos"]!, evento.TempoExecucaoSegundos);
    }

    [Fact]
    public async Task FinalizarAsync_NaoDeveRegistrarNoHistorico_QuandoPublicacaoFalha()
    {
        var execucao = ConfigurarExistente(CriarComExecucaoIniciada());
        var falha = new InvalidOperationException("SQS indisponivel");
        _eventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<ExecucaoFinalizada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await CriarUseCases().FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = execucao.Id });

        Assert.Same(falha, await Assert.ThrowsAsync<InvalidOperationException>(action));
        _gatewayMock.Verify(g => g.AtualizarAsync(execucao, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_historicoMock.Invocations);
    }

    [Fact]
    public async Task FinalizarAsync_DeveLancarSemGravarNemPublicar_QuandoExecucaoNaoIniciada()
    {
        var execucao = ConfigurarExistente(CriarNoStatus(StatusOrdemServico.EmExecucao));

        var action = async () => await CriarUseCases().FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = execucao.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("A execucao da ordem de servico precisa ser iniciada antes de ser finalizada.", exception.Message);
        VerificarSemGravacao();
        Assert.Empty(_eventosPublisherMock.Invocations);
        Assert.Empty(_historicoMock.Invocations);
    }

    [Fact]
    public async Task FinalizarAsync_DeveLancarKeyNotFound_QuandoNaoExiste()
    {
        var action = async () => await CriarUseCases().FinalizarAsync(new FinalizarExecucaoCommand { OrdemServicoId = Guid.NewGuid() });

        await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Empty(_eventosPublisherMock.Invocations);
        Assert.Empty(_historicoMock.Invocations);
    }

    [Fact]
    public async Task ListarHistoricoAsync_DeveRetornarOsEventosEmOrdemCronologica()
    {
        var ordemServicoId = Guid.NewGuid();
        var entrada = new EventoHistoricoExecucao(ordemServicoId, Inicio, TiposHistoricoExecucao.EntrouNaFila, OrigensHistoricoExecucao.Evento, false, null, null);
        var inicio = new EventoHistoricoExecucao(ordemServicoId, Inicio.AddMinutes(30), TiposHistoricoExecucao.ExecucaoIniciada, OrigensHistoricoExecucao.Acao, false, 1800, null);
        var finalizada = new EventoHistoricoExecucao(ordemServicoId, Inicio.AddHours(2), TiposHistoricoExecucao.ExecucaoFinalizada, OrigensHistoricoExecucao.Acao, false, null, 5400);
        _historicoMock
            .Setup(h => h.ListarAsync(ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([finalizada, entrada, inicio]);

        var historico = await CriarUseCases().ListarHistoricoAsync(ordemServicoId);

        Assert.Equal(
            [TiposHistoricoExecucao.EntrouNaFila, TiposHistoricoExecucao.ExecucaoIniciada, TiposHistoricoExecucao.ExecucaoFinalizada],
            historico.Select(evento => evento.Tipo));
        var primeiro = historico.First();
        Assert.Equal(ordemServicoId, primeiro.OrdemServicoId);
        Assert.Equal(Inicio, primeiro.OcorridoEm);
        Assert.Equal("evento", primeiro.Origem);
        Assert.False(primeiro.Repeticao);
        Assert.Equal(1800, historico.ElementAt(1).TempoNaFilaSegundos);
        Assert.Equal(5400, historico.Last().TempoExecucaoSegundos);
    }

    [Fact]
    public async Task ListarHistoricoAsync_DeveRetornarListaVazia_QuandoNaoHaHistorico()
    {
        var ordemServicoId = Guid.NewGuid();
        _historicoMock
            .Setup(h => h.ListarAsync(ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var historico = await CriarUseCases().ListarHistoricoAsync(ordemServicoId);

        Assert.Empty(historico);
    }
}
