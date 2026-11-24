namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

public sealed class EventoHistoricoExecucaoViewModel
{
    public Guid OrdemServicoId { get; set; }
    public DateTime OcorridoEm { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Origem { get; set; } = string.Empty;
    public bool Repeticao { get; set; }
    public double? TempoNaFilaSegundos { get; set; }
    public double? TempoExecucaoSegundos { get; set; }
}
