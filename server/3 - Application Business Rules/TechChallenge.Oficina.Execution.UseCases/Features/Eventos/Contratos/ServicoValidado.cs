namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

// Item de servicos do OrdemServicoValidada: {servicoId, nome, valor}. O Execution guarda so o nome.
public sealed record ServicoValidado(Guid ServicoId, string Nome, decimal Valor);
