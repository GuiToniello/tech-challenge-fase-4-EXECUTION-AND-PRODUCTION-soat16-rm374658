using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using TechChallenge.Oficina.Execution.API.Extensions;
using TechChallenge.Oficina.Execution.API.Features.Execucoes;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using Xunit;

namespace TechChallenge.Oficina.Execution.API.Tests.Features;

// Pipeline HTTP em memoria (TestServer) com a mesma autenticacao, autorizacao e rotas do Program, sem banco e sem SQS
public sealed class AutenticacaoTests : IAsyncLifetime
{
    private readonly Mock<IExecucoesController> _controllerMock = new();
    private WebApplication _app = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthSettings:Authority"] = "autenticacao.teste.local",
            ["AuthSettings:Audience"] = "http://localhost:7197"
        });
        builder.Services.AddAuthConfiguration(builder.Configuration);

        // So os checks com a tag "ready" entram no /health/ready: o check sem tag (unhealthy) nao pode derrubar a resposta
        builder.Services.AddHealthChecks()
            .AddCheck("banco", () => HealthCheckResult.Healthy(), tags: ["ready"])
            .AddCheck("fora-do-ready", () => HealthCheckResult.Unhealthy());
        builder.Services.AddScoped(_ => _controllerMock.Object);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapHealthEndpoints();
        _app.MapExecucaoEndpoints();

        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Health_DeveResponderOkSemToken()
    {
        var resposta = await _cliente.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthReady_DeveResponderSemTokenSoComOsChecksReady()
    {
        var resposta = await _cliente.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET", "/api/execucoes/fila")]
    [InlineData("GET", "/api/execucoes/6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    [InlineData("POST", "/api/execucoes/6f9619ff-8b86-d011-b42d-00c04fc964ff/iniciar-diagnostico")]
    [InlineData("POST", "/api/execucoes/6f9619ff-8b86-d011-b42d-00c04fc964ff/iniciar")]
    [InlineData("POST", "/api/execucoes/6f9619ff-8b86-d011-b42d-00c04fc964ff/finalizar")]
    public async Task RotasDeExecucoes_DevemResponder401SemToken(string metodo, string rota)
    {
        var resposta = await _cliente.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), rota));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Empty(_controllerMock.Invocations);
    }
}
