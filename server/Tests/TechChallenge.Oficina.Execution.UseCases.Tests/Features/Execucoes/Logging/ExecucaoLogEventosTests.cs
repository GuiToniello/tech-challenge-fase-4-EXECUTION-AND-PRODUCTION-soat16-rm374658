using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using Xunit;

namespace TechChallenge.Oficina.Execution.UseCases.Tests.Features.Execucoes.Logging;

public sealed class ExecucaoLogEventosTests
{
    [Theory]
    [InlineData(StatusOrdemServico.Recebida, "recebida")]
    [InlineData(StatusOrdemServico.EmDiagnostico, "em_diagnostico")]
    [InlineData(StatusOrdemServico.AguardandoAprovacao, "aguardando_aprovacao")]
    [InlineData(StatusOrdemServico.EmExecucao, "em_execucao")]
    [InlineData(StatusOrdemServico.Finalizada, "finalizada")]
    [InlineData(StatusOrdemServico.Entregue, "entregue")]
    [InlineData(StatusOrdemServico.Encerrada, "encerrada")]
    [InlineData(StatusOrdemServico.Cancelada, "cancelada")]
    public void StatusParaLog_DeveRetornarValorDoContrato(StatusOrdemServico status, string valorEsperado)
    {
        Assert.Equal(valorEsperado, ExecucaoLogEventos.StatusParaLog(status));
    }

    [Fact]
    public void StatusParaLog_DeveLancar_QuandoStatusInvalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecucaoLogEventos.StatusParaLog((StatusOrdemServico)99));
    }

    [Fact]
    public void Eventos_DevemManterNomesEIdsDoContrato()
    {
        Assert.Equal((2001, "os.falha"), (ExecucaoLogEventos.OsFalha.Id, ExecucaoLogEventos.OsFalha.Name));
        Assert.Equal((3001, "integracao.falha"), (ExecucaoLogEventos.IntegracaoFalha.Id, ExecucaoLogEventos.IntegracaoFalha.Name));
        Assert.Equal((4001, "evento.publicado"), (ExecucaoLogEventos.EventoPublicado.Id, ExecucaoLogEventos.EventoPublicado.Name));
        Assert.Equal((4002, "evento.consumido"), (ExecucaoLogEventos.EventoConsumido.Id, ExecucaoLogEventos.EventoConsumido.Name));
        Assert.Equal((4003, "evento.ignorado"), (ExecucaoLogEventos.EventoIgnorado.Id, ExecucaoLogEventos.EventoIgnorado.Name));
        Assert.Equal((6001, "execucao.diagnostico_iniciado"), (ExecucaoLogEventos.ExecucaoDiagnosticoIniciado.Id, ExecucaoLogEventos.ExecucaoDiagnosticoIniciado.Name));
        Assert.Equal((6002, "execucao.iniciada"), (ExecucaoLogEventos.ExecucaoIniciada.Id, ExecucaoLogEventos.ExecucaoIniciada.Name));
        Assert.Equal((6003, "execucao.finalizada"), (ExecucaoLogEventos.ExecucaoFinalizada.Id, ExecucaoLogEventos.ExecucaoFinalizada.Name));
    }

    [Fact]
    public void Etapas_TiposIntegracoesEOperacoes_DevemManterValoresDoContrato()
    {
        Assert.Equal(
            ["diagnostico", "execucao", "finalizacao"],
            new[] { ExecucaoLogEventos.Etapas.Diagnostico, ExecucaoLogEventos.Etapas.Execucao, ExecucaoLogEventos.Etapas.Finalizacao });
        Assert.Equal(
            ["regra_negocio", "nao_encontrado"],
            new[] { ExecucaoLogEventos.TiposFalha.RegraNegocio, ExecucaoLogEventos.TiposFalha.NaoEncontrado });
        Assert.Equal(
            ["sqs", "postgres"],
            new[] { ExecucaoLogEventos.Integracoes.Sqs, ExecucaoLogEventos.Integracoes.Postgres });
        Assert.Equal(
            ["publicacao", "consumo"],
            new[] { ExecucaoLogEventos.Operacoes.Publicacao, ExecucaoLogEventos.Operacoes.Consumo });
    }
}
