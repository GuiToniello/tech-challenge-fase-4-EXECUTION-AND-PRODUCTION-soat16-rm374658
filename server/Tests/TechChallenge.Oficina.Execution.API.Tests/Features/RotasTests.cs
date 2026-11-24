using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TechChallenge.Oficina.Execution.API.Extensions;
using TechChallenge.Oficina.Execution.API.Features.Execucoes;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using Xunit;

namespace TechChallenge.Oficina.Execution.API.Tests.Features;

public sealed class RotasTests
{
    private static List<RouteEndpoint> MapearRotas()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHealthChecks();
        builder.Services.AddScoped(_ => Mock.Of<IExecucoesController>());
        var app = builder.Build();

        app.MapHealthEndpoints();
        app.MapExecucaoEndpoints();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(fonte => fonte.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static string Descrever(RouteEndpoint endpoint) =>
        $"{endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single()} {endpoint.RoutePattern.RawText}";

    private static List<RouteEndpoint> RotasDeExecucoes() =>
        MapearRotas().Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/execucoes", StringComparison.Ordinal)).ToList();

    [Fact]
    public void Endpoints_DevemMapearAsRotasDeExecucoes()
    {
        var rotas = RotasDeExecucoes().Select(Descrever).OrderBy(rota => rota).ToArray();

        Assert.Equal(
            new[]
            {
                "GET /api/execucoes/fila",
                "GET /api/execucoes/{ordemServicoId:guid}",
                "GET /api/execucoes/{ordemServicoId:guid}/historico",
                "POST /api/execucoes/{ordemServicoId:guid}/finalizar",
                "POST /api/execucoes/{ordemServicoId:guid}/iniciar",
                "POST /api/execucoes/{ordemServicoId:guid}/iniciar-diagnostico"
            }.OrderBy(rota => rota),
            rotas);
    }

    [Fact]
    public void Health_DeveSerAnonimoESomenteEle()
    {
        // Probes do Kubernetes e HEALTHCHECK do container nao enviam JWT; as rotas de negocio seguem a politica padrao (usuario autenticado)
        var anonimas = MapearRotas()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .OrderBy(rota => rota);

        Assert.Equal(["/health", "/health/ready"], anonimas);
    }

    [Fact]
    public void RotasDeExecucoes_NaoDevemPermitirAcessoAnonimo()
    {
        Assert.All(RotasDeExecucoes(), endpoint => Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
    }

    [Fact]
    public void GetById_DeveTerARotaNomeada()
    {
        var endpoint = Assert.Single(MapearRotas(), rota => rota.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetExecucaoByOrdemServicoId");

        Assert.Equal("/api/execucoes/{ordemServicoId:guid}", endpoint.RoutePattern.RawText);
    }
}
