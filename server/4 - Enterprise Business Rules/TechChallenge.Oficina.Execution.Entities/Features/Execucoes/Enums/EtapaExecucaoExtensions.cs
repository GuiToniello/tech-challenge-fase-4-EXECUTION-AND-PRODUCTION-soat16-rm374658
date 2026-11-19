namespace TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;

public static class EtapaExecucaoExtensions
{
    public static string ObterDescricao(this EtapaExecucao etapa)
    {
        return etapa switch
        {
            EtapaExecucao.AguardandoDiagnostico => "Aguardando diagnóstico",
            EtapaExecucao.EmDiagnostico => "Em diagnóstico",
            EtapaExecucao.AguardandoAprovacao => "Aguardando aprovação",
            EtapaExecucao.AguardandoExecucao => "Aguardando execução",
            EtapaExecucao.EmExecucao => "Em execução",
            EtapaExecucao.Finalizada => "Finalizada",
            EtapaExecucao.ForaDaProducao => "Fora da produção",
            _ => throw new ArgumentOutOfRangeException(nameof(etapa), etapa, "Etapa da execucao invalida.")
        };
    }
}
