namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

// Consumido: o OS Service excluiu a OS. O item sai da producao, mas o registro fica (tombstone) para um evento atrasado nao recolocar a OS na fila.
public sealed record OrdemServicoExcluida(Guid OrdemServicoId);
