namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;

// Um registro da linha do tempo da producao da OS. Tipo e Origem usam os valores de TiposHistoricoExecucao e OrigensHistoricoExecucao.
// Repeticao: acao repetida antes da confirmacao do OS Service (o evento foi publicado de novo). Os tempos, em segundos, sao os mesmos dos logs
// execucao.iniciada (TempoNaFilaSegundos) e execucao.finalizada (TempoExecucaoSegundos); nos outros tipos ficam vazios. Sem dados pessoais.
public sealed record EventoHistoricoExecucao(
    Guid OrdemServicoId,
    DateTime OcorridoEm,
    string Tipo,
    string Origem,
    bool Repeticao,
    double? TempoNaFilaSegundos,
    double? TempoExecucaoSegundos);
