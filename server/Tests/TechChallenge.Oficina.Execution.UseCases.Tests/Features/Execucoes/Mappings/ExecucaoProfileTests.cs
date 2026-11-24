using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Mappings;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Execution.UseCases.Tests.Features.Execucoes.Mappings;

public sealed class ExecucaoProfileTests
{
    private static readonly DateTime Inicio = new(2026, 11, 19, 8, 00, 00, DateTimeKind.Utc);

    private readonly IMapper _mapper;

    public ExecucaoProfileTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(ExecucaoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
    }

    [Fact]
    public void Configuracao_DeveMapearTodosOsCamposDoViewModel()
    {
        _mapper.ConfigurationProvider.AssertConfigurationIsValid();
    }

    [Fact]
    public void Map_ExecucaoParaViewModel_DeveMapearDadosStatusEtapaEDatas()
    {
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();
        var execucao = Execucao.CriarPorValidacao(ordemServicoId, clienteId, veiculoId, ["Troca de oleo", "Alinhamento"], Inicio);
        execucao.IniciarDiagnostico(Inicio.AddMinutes(5));
        execucao.AtualizarStatus(StatusOrdemServico.EmExecucao, Inicio.AddMinutes(30));
        execucao.IniciarExecucao(Inicio.AddMinutes(40));
        execucao.Finalizar(Inicio.AddMinutes(90));

        var viewModel = _mapper.Map<ExecucaoViewModel>(execucao);

        Assert.Equal(ordemServicoId, viewModel.OrdemServicoId);
        Assert.Equal(clienteId, viewModel.ClienteId);
        Assert.Equal(veiculoId, viewModel.VeiculoId);
        Assert.Equal(["Troca de oleo", "Alinhamento"], viewModel.Servicos);
        Assert.Equal(4, viewModel.StatusOrdemServico);
        Assert.Equal("Em execução", viewModel.StatusOrdemServicoDescricao);
        Assert.Equal(6, viewModel.Etapa);
        Assert.Equal("Finalizada", viewModel.EtapaDescricao);
        Assert.Equal(Inicio, viewModel.CriadoEm);
        Assert.Equal(Inicio.AddMinutes(5), viewModel.DiagnosticoIniciadoEm);
        Assert.Equal(Inicio.AddMinutes(30), viewModel.EntrouNaFilaEm);
        Assert.Equal(Inicio.AddMinutes(40), viewModel.ExecucaoIniciadaEm);
        Assert.Equal(Inicio.AddMinutes(90), viewModel.FinalizadaEm);
    }

    [Fact]
    public void Map_ItemRemovido_DeveMapearEtapaForaDaProducao()
    {
        var viewModel = _mapper.Map<ExecucaoViewModel>(Execucao.CriarRemovida(Guid.NewGuid(), Inicio));

        Assert.Equal(7, viewModel.Etapa);
        Assert.Equal("Fora da produção", viewModel.EtapaDescricao);
        Assert.Empty(viewModel.Servicos);
    }

    [Fact]
    public void Map_EventoHistoricoExecucaoParaViewModel_DeveCopiarTodosOsCampos()
    {
        var ocorridoEm = new DateTime(2026, 11, 24, 10, 15, 30, 123, DateTimeKind.Utc);
        var evento = new EventoHistoricoExecucao(Guid.NewGuid(), ocorridoEm, "ExecucaoFinalizada", "acao", true, 1800.5, 5400.25);

        var viewModel = _mapper.Map<EventoHistoricoExecucaoViewModel>(evento);

        Assert.Equal(evento.OrdemServicoId, viewModel.OrdemServicoId);
        Assert.Equal(ocorridoEm, viewModel.OcorridoEm);
        Assert.Equal("ExecucaoFinalizada", viewModel.Tipo);
        Assert.Equal("acao", viewModel.Origem);
        Assert.True(viewModel.Repeticao);
        Assert.Equal(1800.5, viewModel.TempoNaFilaSegundos);
        Assert.Equal(5400.25, viewModel.TempoExecucaoSegundos);
    }
}
