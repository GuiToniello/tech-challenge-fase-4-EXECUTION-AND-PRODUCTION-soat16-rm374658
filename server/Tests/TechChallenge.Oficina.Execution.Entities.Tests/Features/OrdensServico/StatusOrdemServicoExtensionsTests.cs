using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using Xunit;

namespace TechChallenge.Oficina.Execution.Entities.Tests.Features.OrdensServico;

public sealed class StatusOrdemServicoExtensionsTests
{
    [Theory]
    [InlineData(StatusOrdemServico.Recebida, 1, "Recebida")]
    [InlineData(StatusOrdemServico.EmDiagnostico, 2, "Em diagnóstico")]
    [InlineData(StatusOrdemServico.AguardandoAprovacao, 3, "Aguardando aprovação")]
    [InlineData(StatusOrdemServico.EmExecucao, 4, "Em execução")]
    [InlineData(StatusOrdemServico.Finalizada, 5, "Finalizada")]
    [InlineData(StatusOrdemServico.Entregue, 6, "Entregue")]
    [InlineData(StatusOrdemServico.Encerrada, 7, "Encerrada")]
    [InlineData(StatusOrdemServico.Cancelada, 8, "Cancelada")]
    public void ObterDescricao_DeveRetornarDescricaoEManterOValorDoContrato(StatusOrdemServico status, int valor, string descricao)
    {
        Assert.Equal(valor, (int)status);
        Assert.Equal(descricao, status.ObterDescricao());
    }

    [Fact]
    public void ObterDescricao_DeveLancar_QuandoStatusInvalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StatusOrdemServico)99).ObterDescricao());
    }
}
