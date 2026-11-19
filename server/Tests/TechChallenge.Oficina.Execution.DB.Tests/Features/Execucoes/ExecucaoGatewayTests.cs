using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Execution.DB.Data.Context;
using TechChallenge.Oficina.Execution.DB.Data.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using Xunit;

namespace TechChallenge.Oficina.Execution.DB.Data.Tests.Features.Execucoes;

public sealed class ExecucaoGatewayTests
{
    private static readonly DateTime Inicio = new(2026, 11, 19, 8, 00, 00, DateTimeKind.Utc);

    private static Execucao CriarRecebida()
        => Execucao.CriarPorValidacao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ["Troca de oleo"], Inicio);

    private static Execucao CriarNoStatus(StatusOrdemServico status)
    {
        var execucao = CriarRecebida();
        execucao.AtualizarStatus(status, Inicio.AddMinutes(1));
        return execucao;
    }

    private static ExecucaoDbContext CriarContexto(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<ExecucaoDbContext>()
            .UseInMemoryDatabase(nomeBanco)
            .Options;

        return new ExecucaoDbContext(options);
    }

    private static async Task GravarAsync(string nomeBanco, params Execucao[] execucoes)
    {
        await using var context = CriarContexto(nomeBanco);
        var gateway = new ExecucaoGateway(context);

        foreach (var execucao in execucoes)
        {
            await gateway.AdicionarAsync(execucao);
        }
    }

    [Fact]
    public async Task AdicionarAsync_DevePersistirItemComOsNomesDosServicosNaOrdem()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var execucao = Execucao.CriarPorValidacao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ["Troca de oleo", "Alinhamento", "Balanceamento"], Inicio);

        await GravarAsync(nomeBanco, execucao);

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new ExecucaoGateway(novoContexto).ObterPorIdAsync(execucao.Id);

        Assert.NotNull(salva);
        Assert.Equal(execucao.ClienteId, salva!.ClienteId);
        Assert.Equal(execucao.VeiculoId, salva.VeiculoId);
        Assert.Equal(["Troca de oleo", "Alinhamento", "Balanceamento"], salva.Servicos);
        Assert.Equal(StatusOrdemServico.Recebida, salva.StatusOrdemServico);
        Assert.Equal(Inicio, salva.CriadoEm);
        Assert.Equal(EtapaExecucao.AguardandoDiagnostico, salva.Etapa);
    }

    [Fact]
    public async Task AdicionarAsync_DevePersistirItemParcialComListaDeServicosVazia()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var parcial = Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmExecucao, Inicio);

        await GravarAsync(nomeBanco, parcial);

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new ExecucaoGateway(novoContexto).ObterPorIdAsync(parcial.Id);

        Assert.NotNull(salva);
        Assert.Empty(salva!.Servicos);
        Assert.Equal(Guid.Empty, salva.VeiculoId);
        Assert.Equal(Inicio, salva.EntrouNaFilaEm);
        Assert.Equal(EtapaExecucao.AguardandoExecucao, salva.Etapa);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirStatusDatasEServicos()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var execucao = CriarRecebida();

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new ExecucaoGateway(context);
            await gateway.AdicionarAsync(execucao);

            var carregada = await gateway.ObterPorIdAsync(execucao.Id);
            carregada!.AtualizarDados(carregada.ClienteId, carregada.VeiculoId, ["Revisao completa"]);
            carregada.AtualizarStatus(StatusOrdemServico.EmExecucao, Inicio.AddHours(1));
            carregada.IniciarExecucao(Inicio.AddHours(2));
            carregada.Finalizar(Inicio.AddHours(3));
            await gateway.AtualizarAsync(carregada);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new ExecucaoGateway(novoContexto).ObterPorIdAsync(execucao.Id);

        Assert.Equal(["Revisao completa"], salva!.Servicos);
        Assert.Equal(StatusOrdemServico.EmExecucao, salva.StatusOrdemServico);
        Assert.Equal(Inicio.AddHours(1), salva.EntrouNaFilaEm);
        Assert.Equal(Inicio.AddHours(2), salva.ExecucaoIniciadaEm);
        Assert.Equal(Inicio.AddHours(3), salva.FinalizadaEm);
        Assert.Equal(EtapaExecucao.Finalizada, salva.Etapa);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarNull_QuandoNaoExiste()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());

        var encontrada = await new ExecucaoGateway(context).ObterPorIdAsync(Guid.NewGuid());

        Assert.Null(encontrada);
    }

    [Fact]
    public async Task ListarFilaAsync_DeveRetornarSoOsItensDasEtapasDaFila()
    {
        var nomeBanco = Guid.NewGuid().ToString();

        var aguardandoDiagnostico = CriarRecebida();
        var diagnosticoPedido = CriarRecebida();
        diagnosticoPedido.IniciarDiagnostico(Inicio.AddMinutes(5));
        var emDiagnostico = CriarNoStatus(StatusOrdemServico.EmDiagnostico);
        var aguardandoExecucao = CriarNoStatus(StatusOrdemServico.EmExecucao);
        var emExecucao = CriarNoStatus(StatusOrdemServico.EmExecucao);
        emExecucao.IniciarExecucao(Inicio.AddMinutes(10));

        var aguardandoAprovacao = CriarNoStatus(StatusOrdemServico.AguardandoAprovacao);
        var finalizadaAqui = CriarNoStatus(StatusOrdemServico.EmExecucao);
        finalizadaAqui.IniciarExecucao(Inicio.AddMinutes(10));
        finalizadaAqui.Finalizar(Inicio.AddMinutes(20));
        var finalizadaNoOsService = CriarNoStatus(StatusOrdemServico.Finalizada);
        var cancelada = Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.Cancelada, Inicio);
        var removida = CriarRecebida();
        removida.Remover(Inicio.AddMinutes(30));
        var tombstone = Execucao.CriarRemovida(Guid.NewGuid(), Inicio);

        await GravarAsync(
            nomeBanco,
            aguardandoDiagnostico, diagnosticoPedido, emDiagnostico, aguardandoExecucao, emExecucao,
            aguardandoAprovacao, finalizadaAqui, finalizadaNoOsService, cancelada, removida, tombstone);

        await using var novoContexto = CriarContexto(nomeBanco);
        var fila = await new ExecucaoGateway(novoContexto).ListarFilaAsync();

        Assert.Equal(
            new[] { aguardandoDiagnostico.Id, diagnosticoPedido.Id, emDiagnostico.Id, aguardandoExecucao.Id, emExecucao.Id }.Order(),
            fila.Select(execucao => execucao.Id).Order());
    }

    [Fact]
    public async Task ListarFilaAsync_DeveRetornarVazia_QuandoNaoHaItensNaFila()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        await GravarAsync(nomeBanco, CriarNoStatus(StatusOrdemServico.AguardandoAprovacao), Execucao.CriarRemovida(Guid.NewGuid(), Inicio));

        await using var novoContexto = CriarContexto(nomeBanco);
        var fila = await new ExecucaoGateway(novoContexto).ListarFilaAsync();

        Assert.Empty(fila);
    }
}
