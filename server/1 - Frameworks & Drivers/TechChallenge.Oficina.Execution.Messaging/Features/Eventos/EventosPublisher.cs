using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Execution.Messaging.Configuration;
using TechChallenge.Oficina.Execution.Messaging.Sqs;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;

namespace TechChallenge.Oficina.Execution.Messaging.Features.Eventos;

// Publica cada evento do Execution nas filas FIFO configuradas (hoje so a do OS Service): o envelope e montado uma vez e enviado a cada fila,
// com o mesmo id (a deduplicacao do SQS e por fila). Uma falha de envio e registrada e relancada.
public sealed class EventosPublisher : IEventosPublisher
{
    public const string Origem = "execution";
    public const int Versao = 1;

    private readonly ISqsClient _sqsClient;
    private readonly SqsSettings _sqsSettings;
    private readonly ILogger<EventosPublisher> _logger;

    // A URL de cada fila nao muda durante a execucao: e resolvida uma vez e reaproveitada (o publisher e singleton)
    private readonly ConcurrentDictionary<string, string> _urlsFilas = new();

    public EventosPublisher(ISqsClient sqsClient, SqsSettings sqsSettings, ILogger<EventosPublisher> logger)
    {
        _sqsClient = sqsClient;
        _sqsSettings = sqsSettings;
        _logger = logger;
    }

    public async Task PublicarAsync<TEvento>(TEvento evento, Guid ordemServicoId, CancellationToken cancellationToken = default) where TEvento : class
    {
        var envelope = new EnvelopeEvento<TEvento>(Guid.NewGuid(), typeof(TEvento).Name, Versao, DateTime.UtcNow, Origem, evento);
        var corpo = JsonSerializer.Serialize(envelope, SerializacaoEventos.Opcoes);

        foreach (var fila in _sqsSettings.FilasPublicacao)
        {
            try
            {
                var urlFila = await ObterUrlFilaAsync(fila, cancellationToken);

                // Grupo = ordem de servico (ordem garantida por OS na fila FIFO); deduplicacao = id do envelope
                await _sqsClient.SendAsync(
                    urlFila,
                    corpo,
                    envelope.Tipo,
                    ordemServicoId.ToString("D"),
                    envelope.Id.ToString("D"),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(
                    ExecucaoLogEventos.IntegracaoFalha,
                    exception,
                    "Falha na integracao {Integracao} durante a operacao {Operacao} do evento {Tipo} da ordem de servico {OrdemServicoId} na fila {Fila}.",
                    ExecucaoLogEventos.Integracoes.Sqs,
                    ExecucaoLogEventos.Operacoes.Publicacao,
                    envelope.Tipo,
                    ordemServicoId,
                    fila);
                throw;
            }

            _logger.LogInformation(
                ExecucaoLogEventos.EventoPublicado,
                "Evento {Tipo} {EventoId} publicado na fila {Fila} para a ordem de servico {OrdemServicoId}.",
                envelope.Tipo,
                envelope.Id,
                fila,
                ordemServicoId);
        }
    }

    private async Task<string> ObterUrlFilaAsync(string fila, CancellationToken cancellationToken)
    {
        if (_urlsFilas.TryGetValue(fila, out var urlFila))
        {
            return urlFila;
        }

        urlFila = await _sqsClient.GetQueueUrlAsync(fila, cancellationToken);
        _urlsFilas[fila] = urlFila;
        return urlFila;
    }
}
