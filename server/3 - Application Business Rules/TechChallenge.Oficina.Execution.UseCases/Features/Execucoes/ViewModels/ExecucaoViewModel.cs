namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

public sealed class ExecucaoViewModel
{
    public Guid OrdemServicoId { get; set; }
    public Guid ClienteId { get; set; }
    public Guid VeiculoId { get; set; }
    public IReadOnlyCollection<string> Servicos { get; set; } = [];
    public int StatusOrdemServico { get; set; }
    public string StatusOrdemServicoDescricao { get; set; } = string.Empty;
    public int Etapa { get; set; }
    public string EtapaDescricao { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public DateTime? DiagnosticoIniciadoEm { get; set; }
    public DateTime? EntrouNaFilaEm { get; set; }
    public DateTime? ExecucaoIniciadaEm { get; set; }
    public DateTime? FinalizadaEm { get; set; }
}
