using System.Text.Json;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

namespace TechChallenge.Oficina.Execution.Integration.Tests.Infrastructure;

/// <summary>
/// Simula os eventos da Management e do OS Service como chegam da fila (dados JSON em camelCase, como no envelope SQS).
/// </summary>
internal static class CenarioOrdemServico
{
    public const string NomeServico = "Troca de Vela";

    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    // OrdemServicoValidada da Management: um servico por padrao
    public static async Task<Guid> ValidarOrdemServicoAsync(IntegrationTestFixture fixture, Guid? ordemServicoId = null, params string[] nomesServicos)
    {
        var id = ordemServicoId ?? Guid.NewGuid();
        var servicos = (nomesServicos.Length > 0 ? nomesServicos : [NomeServico])
            .Select(nome => new ServicoValidado(Guid.NewGuid(), nome, 60m))
            .ToArray();

        await ProcessarAsync(fixture, new OrdemServicoValidada(id, Guid.NewGuid(), Guid.NewGuid(), "cliente@email.com", true, servicos, 60m * servicos.Length));
        return id;
    }

    public static Task<ResultadoProcessamentoEvento> AlterarStatusAsync(IntegrationTestFixture fixture, Guid ordemServicoId, int statusAnterior, int statusNovo)
        => ProcessarAsync(fixture, new OrdemServicoStatusAlterado(ordemServicoId, Guid.NewGuid(), statusAnterior, statusNovo));

    public static Task<ResultadoProcessamentoEvento> ExcluirOrdemServicoAsync(IntegrationTestFixture fixture, Guid ordemServicoId)
        => ProcessarAsync(fixture, new OrdemServicoExcluida(ordemServicoId));

    public static Task<ResultadoProcessamentoEvento> ProcessarAsync<TEvento>(IntegrationTestFixture fixture, TEvento evento)
        => fixture.CriarEventosController().ProcessarAsync(typeof(TEvento).Name, JsonSerializer.Serialize(evento, OpcoesJson), CancellationToken.None);
}
