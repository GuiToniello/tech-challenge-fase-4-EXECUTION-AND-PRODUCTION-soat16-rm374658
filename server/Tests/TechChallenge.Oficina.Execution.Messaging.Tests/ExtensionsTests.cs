using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechChallenge.Oficina.Execution.Messaging.Configuration;
using TechChallenge.Oficina.Execution.Messaging.Features.Eventos;
using TechChallenge.Oficina.Execution.Messaging.Sqs;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Interfaces;
using Xunit;

namespace TechChallenge.Oficina.Execution.Messaging.Tests;

public sealed class ExtensionsTests
{
    private static SqsSettings CriarSettings(string filaConsumo) => new()
    {
        FilaConsumo = filaConsumo,
        FilasPublicacao = ["techchallenge-oficina-os-service.fifo"],
        ServiceUrl = "http://localhost:9327"
    };

    [Fact]
    public void AddInfraMessaging_DeveRegistrarPublisherEClienteSqsApontandoParaServiceUrl()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var settings = CriarSettings("techchallenge-oficina-execution.fifo");

        services.AddInfraMessaging(settings);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<EventosPublisher>(provider.GetRequiredService<IEventosPublisher>());
        Assert.IsType<SqsClientAdapter>(provider.GetRequiredService<ISqsClient>());
        Assert.Same(settings, provider.GetRequiredService<SqsSettings>());
        var clienteSqs = provider.GetRequiredService<IAmazonSQS>();
        Assert.StartsWith("http://localhost:9327", clienteSqs.Config.ServiceURL);
    }

    [Fact]
    public void AddInfraMessaging_DeveRegistrarConsumidor_QuandoFilaDeConsumoConfigurada()
    {
        var services = new ServiceCollection();

        services.AddInfraMessaging(CriarSettings("techchallenge-oficina-execution.fifo"));

        Assert.Contains(services, descritor => descritor.ServiceType == typeof(IHostedService) && descritor.ImplementationType == typeof(SqsConsumidorHostedService));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfraMessaging_NaoDeveRegistrarConsumidor_QuandoFilaDeConsumoVazia(string filaConsumo)
    {
        var services = new ServiceCollection();

        services.AddInfraMessaging(CriarSettings(filaConsumo));

        Assert.DoesNotContain(services, descritor => descritor.ServiceType == typeof(IHostedService));
        Assert.Contains(services, descritor => descritor.ServiceType == typeof(IEventosPublisher));
    }
}
