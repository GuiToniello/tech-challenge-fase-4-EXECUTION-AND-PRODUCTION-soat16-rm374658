namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

// Consumido: publicado pelo OS Service a cada mudanca de status. Status com os valores de StatusOrdemServico (1 a 8).
public sealed record OrdemServicoStatusAlterado(Guid OrdemServicoId, Guid ClienteId, int StatusAnterior, int StatusNovo);
