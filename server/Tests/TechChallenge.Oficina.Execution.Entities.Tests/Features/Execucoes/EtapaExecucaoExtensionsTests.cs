using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using Xunit;

namespace TechChallenge.Oficina.Execution.Entities.Tests.Features.Execucoes;

public sealed class EtapaExecucaoExtensionsTests
{
    [Theory]
    [InlineData(EtapaExecucao.AguardandoDiagnostico, 1, "Aguardando diagnóstico")]
    [InlineData(EtapaExecucao.EmDiagnostico, 2, "Em diagnóstico")]
    [InlineData(EtapaExecucao.AguardandoAprovacao, 3, "Aguardando aprovação")]
    [InlineData(EtapaExecucao.AguardandoExecucao, 4, "Aguardando execução")]
    [InlineData(EtapaExecucao.EmExecucao, 5, "Em execução")]
    [InlineData(EtapaExecucao.Finalizada, 6, "Finalizada")]
    [InlineData(EtapaExecucao.ForaDaProducao, 7, "Fora da produção")]
    public void ObterDescricao_DeveRetornarDescricaoEManterOValorDoContrato(EtapaExecucao etapa, int valor, string descricao)
    {
        Assert.Equal(valor, (int)etapa);
        Assert.Equal(descricao, etapa.ObterDescricao());
    }

    [Fact]
    public void ObterDescricao_DeveLancar_QuandoEtapaInvalida()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((EtapaExecucao)99).ObterDescricao());
    }
}
