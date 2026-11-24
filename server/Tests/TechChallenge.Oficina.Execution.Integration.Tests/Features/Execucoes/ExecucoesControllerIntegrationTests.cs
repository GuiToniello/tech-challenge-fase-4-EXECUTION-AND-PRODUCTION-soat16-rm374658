using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Execution.Integration.Tests.Features.Execucoes;

// Acoes do mecanico (HTTP) sobre itens criados pelos eventos da saga, com DI real e banco InMemory
public sealed class ExecucoesControllerIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<ExecucaoViewModel> ObterAsync(Guid ordemServicoId)
        => Assert.IsType<Ok<ExecucaoViewModel>>(await _fixture.CriarExecucoesEndpoints().GetById(ordemServicoId, CancellationToken.None)).Value!;

    private async Task<IReadOnlyCollection<ExecucaoViewModel>> ObterFilaAsync()
        => Assert.IsType<Ok<IReadOnlyCollection<ExecucaoViewModel>>>(await _fixture.CriarExecucoesEndpoints().GetFila(CancellationToken.None)).Value!;

    private Task<object> IniciarDiagnosticoAsync(Guid ordemServicoId) => _fixture.CriarExecucoesEndpoints().IniciarDiagnostico(ordemServicoId, CancellationToken.None);

    private Task<object> IniciarAsync(Guid ordemServicoId) => _fixture.CriarExecucoesEndpoints().Iniciar(ordemServicoId, CancellationToken.None);

    private Task<object> FinalizarAsync(Guid ordemServicoId) => _fixture.CriarExecucoesEndpoints().Finalizar(ordemServicoId, CancellationToken.None);

    private async Task<EventoHistoricoExecucaoViewModel[]> ObterHistoricoAsync(Guid ordemServicoId)
        => Assert.IsType<Ok<IReadOnlyCollection<EventoHistoricoExecucaoViewModel>>>(await _fixture.CriarExecucoesEndpoints().GetHistorico(ordemServicoId, CancellationToken.None)).Value!.ToArray();

    [Fact]
    public async Task GetHistorico_DaEntradaNaFilaAteAFinalizacao_DeveRetornarALinhaDoTempoEmOrdem()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 3, 4);
        // Pagamento aprovado entregue de novo pela fila: ignorado, sem segunda entrada na linha do tempo
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 3, 4));
        var emExecucao = Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarAsync(ordemServicoId)).Value!;
        var finalizada = Assert.IsType<Ok<ExecucaoViewModel>>(await FinalizarAsync(ordemServicoId)).Value!;

        var historico = await ObterHistoricoAsync(ordemServicoId);

        Assert.Equal(["EntrouNaFila", "ExecucaoIniciada", "ExecucaoFinalizada"], historico.Select(evento => evento.Tipo));
        Assert.Equal(["evento", "acao", "acao"], historico.Select(evento => evento.Origem));
        Assert.All(historico, evento =>
        {
            Assert.Equal(ordemServicoId, evento.OrdemServicoId);
            Assert.False(evento.Repeticao);
        });
        // Os instantes sao as datas gravadas no PostgreSQL e os tempos sao os mesmos dos logs
        Assert.Equal(emExecucao.EntrouNaFilaEm, historico[0].OcorridoEm);
        Assert.Equal(emExecucao.ExecucaoIniciadaEm, historico[1].OcorridoEm);
        Assert.Equal(finalizada.FinalizadaEm, historico[2].OcorridoEm);
        Assert.Equal((emExecucao.ExecucaoIniciadaEm!.Value - emExecucao.EntrouNaFilaEm!.Value).TotalSeconds, historico[1].TempoNaFilaSegundos);
        Assert.Equal((finalizada.FinalizadaEm!.Value - finalizada.ExecucaoIniciadaEm!.Value).TotalSeconds, historico[2].TempoExecucaoSegundos);
        Assert.Null(historico[0].TempoNaFilaSegundos);
    }

    [Fact]
    public async Task GetHistorico_DiagnosticoRepetido_DeveRegistrarAsDuasAcoesMarcandoARepeticao()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        var primeiro = Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarDiagnosticoAsync(ordemServicoId)).Value!;
        Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarDiagnosticoAsync(ordemServicoId));

        var historico = await ObterHistoricoAsync(ordemServicoId);

        Assert.Equal(["DiagnosticoIniciado", "DiagnosticoIniciado"], historico.Select(evento => evento.Tipo));
        Assert.Equal([false, true], historico.Select(evento => evento.Repeticao));
        Assert.Equal(primeiro.DiagnosticoIniciadoEm, historico[0].OcorridoEm);
        Assert.All(historico, evento =>
        {
            Assert.Equal("acao", evento.Origem);
            Assert.Null(evento.TempoNaFilaSegundos);
            Assert.Null(evento.TempoExecucaoSegundos);
        });
    }

    [Fact]
    public async Task GetHistorico_AcaoRejeitadaOuOrdemDesconhecida_NaoDeveRegistrarNada()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await IniciarAsync(ordemServicoId));

        Assert.Empty(await ObterHistoricoAsync(ordemServicoId));
        Assert.Empty(await ObterHistoricoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task FluxoCompleto_DaValidacaoAteAFinalizacaoConfirmadaPeloOsService()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        Assert.Equal((int)EtapaExecucao.AguardandoDiagnostico, (await ObterAsync(ordemServicoId)).Etapa);

        // Mecanico inicia o diagnostico: publica DiagnosticoIniciado, mas o status so muda com o evento do OS Service
        var diagnostico = Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarDiagnosticoAsync(ordemServicoId)).Value!;
        Assert.Equal((int)EtapaExecucao.EmDiagnostico, diagnostico.Etapa);
        Assert.Equal(1, diagnostico.StatusOrdemServico);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId, CancellationToken.None), Times.Once);

        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2);
        var emDiagnostico = await ObterAsync(ordemServicoId);
        Assert.Equal(2, emDiagnostico.StatusOrdemServico);
        Assert.Equal((int)EtapaExecucao.EmDiagnostico, emDiagnostico.Etapa);

        // Sem pagamento aprovado nao ha execucao
        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await IniciarAsync(ordemServicoId));
        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await FinalizarAsync(ordemServicoId));

        // Orcamento gerado (aguardando aprovacao): sai da fila
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 2, 3);
        Assert.Equal((int)EtapaExecucao.AguardandoAprovacao, (await ObterAsync(ordemServicoId)).Etapa);
        Assert.Empty(await ObterFilaAsync());

        // Pagamento aprovado: volta para a fila aguardando execucao
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 3, 4);
        var naFila = Assert.Single(await ObterFilaAsync());
        Assert.Equal((int)EtapaExecucao.AguardandoExecucao, naFila.Etapa);
        Assert.NotNull(naFila.EntrouNaFilaEm);

        var emExecucao = Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarAsync(ordemServicoId)).Value!;
        Assert.Equal((int)EtapaExecucao.EmExecucao, emExecucao.Etapa);
        Assert.Equal(4, emExecucao.StatusOrdemServico);

        var finalizada = Assert.IsType<Ok<ExecucaoViewModel>>(await FinalizarAsync(ordemServicoId)).Value!;
        Assert.Equal((int)EtapaExecucao.Finalizada, finalizada.Etapa);
        Assert.Empty(await ObterFilaAsync());

        // Repeticao antes da confirmacao: publica de novo mantendo a data
        var repetida = Assert.IsType<Ok<ExecucaoViewModel>>(await FinalizarAsync(ordemServicoId)).Value!;
        Assert.Equal(finalizada.FinalizadaEm, repetida.FinalizadaEm);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new ExecucaoFinalizada(ordemServicoId), ordemServicoId, CancellationToken.None), Times.Exactly(2));

        // OS Service confirma (4 -> 5): nova finalizacao e rejeitada
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 4, 5);
        var confirmada = await ObterAsync(ordemServicoId);
        Assert.Equal(5, confirmada.StatusOrdemServico);
        Assert.Equal((int)EtapaExecucao.Finalizada, confirmada.Etapa);
        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await FinalizarAsync(ordemServicoId));
    }

    [Fact]
    public async Task IniciarDiagnostico_Repetido_DevePublicarDeNovoMantendoAData()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        var primeiro = Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarDiagnosticoAsync(ordemServicoId)).Value!;

        var segundo = Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarDiagnosticoAsync(ordemServicoId)).Value!;

        Assert.Equal(primeiro.DiagnosticoIniciadoEm, segundo.DiagnosticoIniciadoEm);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new DiagnosticoIniciado(ordemServicoId), ordemServicoId, CancellationToken.None), Times.Exactly(2));
        var gravada = await _fixture.ObterContexto().Execucoes.SingleAsync();
        Assert.Equal(primeiro.DiagnosticoIniciadoEm, gravada.DiagnosticoIniciadoEm);
    }

    [Fact]
    public async Task IniciarDiagnostico_DepoisDaConfirmacao_DeveRetornar400SemPublicar()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2);

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(await IniciarDiagnosticoAsync(ordemServicoId));

        Assert.Equal("Somente ordem de servico recebida pode ter o diagnostico iniciado.", badRequest.Value!["message"]);
        Assert.DoesNotContain(_fixture.EventosPublisherMock.Invocations, invocacao => invocacao.Method.Name == "PublicarAsync");
    }

    [Fact]
    public async Task Iniciar_Repetido_DeveRetornar400SemPublicarEvento()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 3, 4);
        Assert.IsType<Ok<ExecucaoViewModel>>(await IniciarAsync(ordemServicoId));

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(await IniciarAsync(ordemServicoId));

        Assert.Equal("A execucao da ordem de servico ja foi iniciada.", badRequest.Value!["message"]);
        Assert.DoesNotContain(_fixture.EventosPublisherMock.Invocations, invocacao => invocacao.Method.Name == "PublicarAsync");
    }

    [Fact]
    public async Task Acoes_EmOrdemExcluida_DevemRetornar400()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId);

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(await IniciarDiagnosticoAsync(ordemServicoId));

        Assert.Equal("A ordem de servico foi excluida e saiu da producao.", badRequest.Value!["message"]);
    }

    [Fact]
    public async Task Fila_DeveOrdenarPorEtapaEDepoisPelaMaisAntiga()
    {
        var aguardandoDiagnostico = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        var emDiagnostico = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await IniciarDiagnosticoAsync(emDiagnostico);
        var aguardandoExecucao = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, aguardandoExecucao, 3, 4);
        var emExecucao = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, emExecucao, 3, 4);
        await IniciarAsync(emExecucao);
        var aguardandoAprovacao = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, aguardandoAprovacao, 2, 3);

        var fila = await ObterFilaAsync();

        Assert.Equal([emExecucao, aguardandoExecucao, emDiagnostico, aguardandoDiagnostico], fila.Select(item => item.OrdemServicoId));
    }

    [Fact]
    public async Task GetById_OrdemDesconhecida_DeveRetornar404()
    {
        var notFound = Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarExecucoesEndpoints().GetById(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("Execucao da ordem de servico nao encontrada.", notFound.Value!["message"]);
    }

    [Fact]
    public async Task Acoes_OrdemDesconhecida_DevemRetornar404()
    {
        var ordemServicoId = Guid.NewGuid();

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await IniciarDiagnosticoAsync(ordemServicoId));
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await IniciarAsync(ordemServicoId));
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await FinalizarAsync(ordemServicoId));
    }
}
