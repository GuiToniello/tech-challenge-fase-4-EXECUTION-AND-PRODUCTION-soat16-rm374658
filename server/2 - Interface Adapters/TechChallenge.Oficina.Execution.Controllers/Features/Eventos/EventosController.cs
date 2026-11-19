using System.Text.Json;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.UseCases;

namespace TechChallenge.Oficina.Execution.Controllers.Features.Eventos
{
    public sealed class EventosController : IEventosController
    {
        // Os dados do envelope chegam em camelCase (contrato entre os servicos)
        private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

        private readonly IExecucaoEventosUseCases _execucaoEventosUseCases;

        public EventosController(IExecucaoEventosUseCases execucaoEventosUseCases)
        {
            _execucaoEventosUseCases = execucaoEventosUseCases;
        }

        // Tipo desconhecido (inclusive os eventos publicados pelo proprio Execution, DiagnosticoIniciado e ExecucaoFinalizada) e ignorado e a mensagem sai da fila.
        // Dados invalidos lancam JsonException: a mensagem fica na fila e, apos as retentativas, vai para a DLQ.
        public async Task<ResultadoProcessamentoEvento> ProcessarAsync(string tipo, string dadosJson, CancellationToken cancellationToken)
        {
            return tipo switch
            {
                nameof(OrdemServicoValidada) => await _execucaoEventosUseCases.ProcessarValidadaAsync(Desserializar<OrdemServicoValidada>(dadosJson), cancellationToken),
                nameof(OrdemServicoStatusAlterado) => await _execucaoEventosUseCases.ProcessarStatusAlteradoAsync(Desserializar<OrdemServicoStatusAlterado>(dadosJson), cancellationToken),
                nameof(OrdemServicoExcluida) => await _execucaoEventosUseCases.ProcessarExcluidaAsync(Desserializar<OrdemServicoExcluida>(dadosJson), cancellationToken),
                _ => ResultadoProcessamentoEvento.Ignorado
            };
        }

        private static TEvento Desserializar<TEvento>(string dadosJson) where TEvento : class
        {
            return JsonSerializer.Deserialize<TEvento>(dadosJson, OpcoesJson)
                ?? throw new JsonException($"Os dados do evento {typeof(TEvento).Name} nao foram informados.");
        }
    }
}
