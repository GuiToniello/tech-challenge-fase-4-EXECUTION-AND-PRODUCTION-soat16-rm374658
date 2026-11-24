using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;

namespace TechChallenge.Oficina.Execution.Integration.Tests.Infrastructure;

/// <summary>
/// Linha do tempo da producao em memoria no lugar do DynamoDB: uma lista com os eventos na ordem de registro.
/// </summary>
public sealed class HistoricoExecucaoEmMemoria : IHistoricoExecucaoGateway
{
    private readonly List<EventoHistoricoExecucao> _eventos = [];

    public IReadOnlyList<EventoHistoricoExecucao> Eventos => _eventos;

    public Task RegistrarAsync(EventoHistoricoExecucao evento, CancellationToken cancellationToken = default)
    {
        _eventos.Add(evento);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<EventoHistoricoExecucao>> ListarAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<EventoHistoricoExecucao> eventos = _eventos
            .Where(evento => evento.OrdemServicoId == ordemServicoId)
            .OrderBy(evento => evento.OcorridoEm)
            .ToArray();

        return Task.FromResult(eventos);
    }
}
