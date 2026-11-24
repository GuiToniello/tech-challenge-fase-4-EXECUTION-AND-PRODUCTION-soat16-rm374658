using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TechChallenge.Oficina.Execution.Adapters.Features.Execucoes;
using TechChallenge.Oficina.Execution.Controllers.Features.Eventos;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using TechChallenge.Oficina.Execution.DB.Data.Context;
using TechChallenge.Oficina.Execution.DB.Data.Features.Execucoes;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.UseCases;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Mappings;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

namespace TechChallenge.Oficina.Execution.Integration.Tests.Infrastructure;

/// <summary>
/// Fixture de integração: DI real com EF Core InMemory. Só a publicação de eventos no SQS é mock e a linha do tempo (DynamoDB) fica em memória.
/// Cada instância possui banco de dados isolado (nome único por GUID) e cada controller é obtido de um escopo novo, como numa requisição.
/// </summary>
public sealed class IntegrationTestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly List<IServiceScope> _escopos = [];

    public IntegrationTestFixture()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ExecucaoDbContext>(options => options.UseInMemoryDatabase(dbName));

        services.AddScoped<IExecucaoGateway, ExecucaoGateway>();

        EventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<It.IsAnyType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        services.AddSingleton(EventosPublisherMock.Object);
        services.AddSingleton<IHistoricoExecucaoGateway>(HistoricoExecucao);

        services.AddAutoMapper(_ => { }, typeof(ExecucaoProfile).Assembly);
        services.AddScoped<IExecucaoUseCases, ExecucaoUseCases>();
        services.AddScoped<IExecucaoEventosUseCases, ExecucaoEventosUseCases>();
        services.AddScoped<IExecucoesAdapter, ExecucoesAdapter>();
        services.AddScoped<ExecucoesController>();
        services.AddScoped<IEventosController, EventosController>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public Mock<IEventosPublisher> EventosPublisherMock { get; } = new();
    public HistoricoExecucaoEmMemoria HistoricoExecucao { get; } = new();

    private T Obter<T>() where T : notnull
    {
        var escopo = _serviceProvider.CreateScope();
        _escopos.Add(escopo);
        return escopo.ServiceProvider.GetRequiredService<T>();
    }

    public ExecucoesController CriarExecucoesEndpoints()
        => Obter<ExecucoesController>();

    public IEventosController CriarEventosController()
        => Obter<IEventosController>();

    public ExecucaoDbContext ObterContexto()
        => Obter<ExecucaoDbContext>();

    public void Dispose()
    {
        foreach (var escopo in _escopos)
        {
            escopo.Dispose();
        }

        _serviceProvider.Dispose();
    }
}
