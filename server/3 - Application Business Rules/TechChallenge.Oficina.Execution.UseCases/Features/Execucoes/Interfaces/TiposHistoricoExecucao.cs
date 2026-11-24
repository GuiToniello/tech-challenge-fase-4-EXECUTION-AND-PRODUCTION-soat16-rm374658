namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;

// Tipos de evento da linha do tempo da producao (atributo Tipo do item no DynamoDB)
public static class TiposHistoricoExecucao
{
    public const string DiagnosticoIniciado = "DiagnosticoIniciado";
    public const string EntrouNaFila = "EntrouNaFila";
    public const string ExecucaoIniciada = "ExecucaoIniciada";
    public const string ExecucaoFinalizada = "ExecucaoFinalizada";
}
