namespace TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;

// Etapa da OS na producao (oficina). Calculada a partir do status espelhado do OS Service e das datas registradas pelo Execution: nao e gravada.
// A fila de producao mostra AguardandoDiagnostico, EmDiagnostico, AguardandoExecucao e EmExecucao.
public enum EtapaExecucao
{
    AguardandoDiagnostico = 1,
    EmDiagnostico = 2,
    AguardandoAprovacao = 3,
    AguardandoExecucao = 4,
    EmExecucao = 5,
    Finalizada = 6,
    ForaDaProducao = 7
}
