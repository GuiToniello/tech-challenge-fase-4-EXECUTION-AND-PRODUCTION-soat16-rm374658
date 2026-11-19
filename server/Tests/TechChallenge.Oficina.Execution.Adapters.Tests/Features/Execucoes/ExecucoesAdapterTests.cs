using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TechChallenge.Oficina.Execution.Adapters.Features.Execucoes;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Execution.Adapters.Tests.Features.Execucoes;

public sealed class ExecucoesAdapterTests
{
    private readonly ExecucoesAdapter _adapter = new();

    [Fact]
    public void Adapt_DeveRetornarOk_QuandoExecucaoViewModelSucesso()
    {
        var execucao = new ExecucaoViewModel { OrdemServicoId = Guid.NewGuid(), Etapa = 2, EtapaDescricao = "Em diagnóstico" };

        var adaptado = _adapter.Adapt(new ExecucoesResult<ExecucaoViewModel, Exception>(execucao));

        var ok = Assert.IsType<Ok<ExecucaoViewModel>>(adaptado);
        Assert.Same(execucao, ok.Value);
    }

    [Fact]
    public void Adapt_DeveRetornarBadRequestComMensagem_QuandoDomainException()
    {
        var adaptado = _adapter.Adapt(new ExecucoesResult<ExecucaoViewModel, Exception>(new DomainException("Somente ordem de servico em execucao pode ser finalizada.")));

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(adaptado);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal("Somente ordem de servico em execucao pode ser finalizada.", badRequest.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarNotFoundComMensagem_QuandoKeyNotFoundException()
    {
        var adaptado = _adapter.Adapt(new ExecucoesResult<ExecucaoViewModel, Exception>(new KeyNotFoundException("Execucao da ordem de servico nao encontrada.")));

        var notFound = Assert.IsType<NotFound<Dictionary<string, string?>>>(adaptado);
        Assert.Equal("Execucao da ordem de servico nao encontrada.", notFound.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarProblem_QuandoExcecaoInesperada()
    {
        var adaptado = _adapter.Adapt(new ExecucoesResult<ExecucaoViewModel, Exception>(new InvalidOperationException("falha")));

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(adaptado);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode.StatusCode);
    }

    [Fact]
    public void Adapt_DeveRetornarOkComAFila_QuandoListaSucesso()
    {
        IReadOnlyCollection<ExecucaoViewModel> fila = [new ExecucaoViewModel { OrdemServicoId = Guid.NewGuid() }, new ExecucaoViewModel { OrdemServicoId = Guid.NewGuid() }];

        var adaptado = _adapter.Adapt(new ExecucoesResult<IReadOnlyCollection<ExecucaoViewModel>, Exception>(fila));

        var ok = Assert.IsType<Ok<IReadOnlyCollection<ExecucaoViewModel>>>(adaptado);
        Assert.Equal(2, ok.Value!.Count);
    }

    [Fact]
    public void Adapt_DeveRetornarOkComListaVazia_QuandoFilaVazia()
    {
        var adaptado = _adapter.Adapt(new ExecucoesResult<IReadOnlyCollection<ExecucaoViewModel>, Exception>(Array.Empty<ExecucaoViewModel>()));

        var ok = Assert.IsType<Ok<IReadOnlyCollection<ExecucaoViewModel>>>(adaptado);
        Assert.Empty(ok.Value!);
    }

    [Fact]
    public void Adapt_DeveRetornarErro_QuandoListaComExcecao()
    {
        var adaptado = _adapter.Adapt(new ExecucoesResult<IReadOnlyCollection<ExecucaoViewModel>, Exception>(new DomainException("erro de regra")));

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(adaptado);
        Assert.Equal(StatusCodes.Status400BadRequest, statusCode.StatusCode);
    }

    [Fact]
    public void CriaErro_DeveMapearCadaTipoDeExcecao()
    {
        Assert.IsType<BadRequest<Dictionary<string, string?>>>(_adapter.CriaErro(new DomainException("regra")));
        Assert.IsType<NotFound<Dictionary<string, string?>>>(_adapter.CriaErro(new KeyNotFoundException("nao encontrado")));
        Assert.IsType<ProblemHttpResult>(_adapter.CriaErro(new TimeoutException("tempo esgotado")));
    }
}
