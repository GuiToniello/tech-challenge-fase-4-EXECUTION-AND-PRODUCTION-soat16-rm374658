using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;

namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;

// Catalogo dos eventos de log de negocio do Execution. Nomes e valores sao o contrato com as metricas e monitores do Datadog: nao renomear sem atualizar os dois lados.
// os.falha, integracao.falha e evento.* seguem os mesmos nomes dos outros servicos. O os.status_alterado nao e emitido aqui (o OS Service ja o registra).
public static class ExecucaoLogEventos
{
    public static readonly EventId OsFalha = new(2001, "os.falha");
    public static readonly EventId IntegracaoFalha = new(3001, "integracao.falha");
    public static readonly EventId EventoPublicado = new(4001, "evento.publicado");
    public static readonly EventId EventoConsumido = new(4002, "evento.consumido");
    public static readonly EventId EventoIgnorado = new(4003, "evento.ignorado");
    public static readonly EventId ExecucaoDiagnosticoIniciado = new(6001, "execucao.diagnostico_iniciado");
    public static readonly EventId ExecucaoIniciada = new(6002, "execucao.iniciada");
    public static readonly EventId ExecucaoFinalizada = new(6003, "execucao.finalizada");

    public static class Etapas
    {
        public const string Diagnostico = "diagnostico";
        public const string Execucao = "execucao";
        public const string Finalizacao = "finalizacao";
    }

    public static class TiposFalha
    {
        public const string RegraNegocio = "regra_negocio";
        public const string NaoEncontrado = "nao_encontrado";
    }

    public static class Integracoes
    {
        public const string Sqs = "sqs";
        public const string Postgres = "postgres";
        public const string DynamoDb = "dynamodb";
    }

    public static class Operacoes
    {
        public const string Publicacao = "publicacao";
        public const string Consumo = "consumo";
        public const string RegistroHistorico = "registro_historico";
        public const string ConsultaHistorico = "consulta_historico";
    }

    public static string StatusParaLog(StatusOrdemServico status)
    {
        return status switch
        {
            StatusOrdemServico.Recebida => "recebida",
            StatusOrdemServico.EmDiagnostico => "em_diagnostico",
            StatusOrdemServico.AguardandoAprovacao => "aguardando_aprovacao",
            StatusOrdemServico.EmExecucao => "em_execucao",
            StatusOrdemServico.Finalizada => "finalizada",
            StatusOrdemServico.Entregue => "entregue",
            StatusOrdemServico.Encerrada => "encerrada",
            StatusOrdemServico.Cancelada => "cancelada",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status da ordem de servico invalido.")
        };
    }
}
