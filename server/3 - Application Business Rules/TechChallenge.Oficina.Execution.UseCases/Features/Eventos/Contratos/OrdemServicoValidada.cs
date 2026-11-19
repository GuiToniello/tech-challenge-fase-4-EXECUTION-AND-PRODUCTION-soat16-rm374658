namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;

// Consumido: a Management validou cliente, veiculo e servicos da OS (na abertura ou na atualizacao) e envia o snapshot.
// O Execution grava ou atualiza o item da producao (cliente, veiculo e nomes dos servicos) mantendo o status ja conhecido.
public sealed record OrdemServicoValidada(
    Guid OrdemServicoId,
    Guid ClienteId,
    Guid VeiculoId,
    string? ClienteEmail,
    bool EstoqueSuficiente,
    IReadOnlyCollection<ServicoValidado> Servicos,
    decimal ValorTotal);
