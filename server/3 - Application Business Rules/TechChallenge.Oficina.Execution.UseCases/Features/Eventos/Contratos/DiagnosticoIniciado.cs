namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

// Publicado pelo Execution quando o mecanico inicia o diagnostico: o OS Service passa a OS de Recebida para EmDiagnostico.
// Pode ser publicado de novo enquanto o OS Service nao confirmar (status ainda Recebida): o consumidor deve ser idempotente.
public sealed record DiagnosticoIniciado(Guid OrdemServicoId);
