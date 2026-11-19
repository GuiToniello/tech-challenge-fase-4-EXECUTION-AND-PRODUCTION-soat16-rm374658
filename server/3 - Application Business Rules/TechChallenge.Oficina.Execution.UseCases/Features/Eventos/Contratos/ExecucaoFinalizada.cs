namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

// Publicado pelo Execution quando o mecanico finaliza a execucao: o OS Service passa a OS de EmExecucao para Finalizada.
// Pode ser publicado de novo enquanto o OS Service nao confirmar (status ainda EmExecucao): o consumidor deve ser idempotente.
public sealed record ExecucaoFinalizada(Guid OrdemServicoId);
