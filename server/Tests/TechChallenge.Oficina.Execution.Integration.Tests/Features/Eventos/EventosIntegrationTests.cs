using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Execution.Integration.Tests.Features.Eventos;

// Saga do lado do Execution: eventos da Management e do OS Service (JSON como chegam da fila) aplicados pela EventosController
public sealed class EventosIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<IReadOnlyCollection<ExecucaoViewModel>> ObterFilaAsync()
        => Assert.IsType<Ok<IReadOnlyCollection<ExecucaoViewModel>>>(await _fixture.CriarExecucoesEndpoints().GetFila(CancellationToken.None)).Value!;

    [Fact]
    public async Task Validacao_DeveColocarAOrdemNaFilaAguardandoDiagnostico()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, nomesServicos: ["Troca de Vela", "Alinhamento"]);

        var item = Assert.Single(await ObterFilaAsync());
        Assert.Equal(ordemServicoId, item.OrdemServicoId);
        Assert.Equal((int)EtapaExecucao.AguardandoDiagnostico, item.Etapa);
        Assert.Equal(["Troca de Vela", "Alinhamento"], item.Servicos);
    }

    [Fact]
    public async Task StatusAntesDaValidacao_DeveCriarItemParcialECompletarMantendoOStatus()
    {
        var ordemServicoId = Guid.NewGuid();

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2));
        await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, ordemServicoId);

        var execucao = await _fixture.ObterContexto().Execucoes.SingleAsync();
        Assert.Equal(StatusOrdemServico.EmDiagnostico, execucao.StatusOrdemServico);
        Assert.Equal(CenarioOrdemServico.NomeServico, Assert.Single(execucao.Servicos));
        Assert.NotEqual(Guid.Empty, execucao.VeiculoId);
        Assert.Equal(EtapaExecucao.EmDiagnostico, execucao.Etapa);
    }

    [Fact]
    public async Task StatusRepetido_DeveSerIgnorado()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2));
    }

    [Fact]
    public async Task ValidacaoRepetida_DeveAtualizarOsServicosSemDuplicar()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, ordemServicoId, "Balanceamento");

        var execucao = await _fixture.ObterContexto().Execucoes.SingleAsync();
        Assert.Equal("Balanceamento", Assert.Single(execucao.Servicos));
    }

    [Fact]
    public async Task OrdemServicoExcluida_DeveTirarDaFilaEIgnorarARepeticao()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId));

        Assert.Empty(await ObterFilaAsync());
        var execucao = Assert.IsType<Ok<ExecucaoViewModel>>(await _fixture.CriarExecucoesEndpoints().GetById(ordemServicoId, CancellationToken.None)).Value!;
        Assert.Equal((int)EtapaExecucao.ForaDaProducao, execucao.Etapa);
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId));
    }

    [Fact]
    public async Task ExclusaoAntesDaValidacao_NaoDeveColocarAOrdemNaFila()
    {
        var ordemServicoId = Guid.NewGuid();

        await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId);
        await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, ordemServicoId);

        Assert.Empty(await ObterFilaAsync());
        var execucao = await _fixture.ObterContexto().Execucoes.SingleAsync();
        Assert.NotNull(execucao.RemovidaEm);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Fact]
    public async Task CanceladaAntesDaValidacao_NaoDeveColocarAOrdemNaFila()
    {
        var ordemServicoId = Guid.NewGuid();

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 8));
        await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, ordemServicoId);

        Assert.Empty(await ObterFilaAsync());
        var execucao = await _fixture.ObterContexto().Execucoes.SingleAsync();
        Assert.Equal(StatusOrdemServico.Cancelada, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Theory]
    [InlineData(typeof(DiagnosticoIniciado))]
    [InlineData(typeof(ExecucaoFinalizada))]
    public async Task EventoPublicadoPeloProprioExecution_DeveSerIgnorado(Type tipoEvento)
    {
        var resultado = await _fixture.CriarEventosController().ProcessarAsync(tipoEvento.Name, $$"""{"ordemServicoId":"{{Guid.NewGuid()}}"}""", CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.Empty(await _fixture.ObterContexto().Execucoes.ToListAsync());
    }
}
