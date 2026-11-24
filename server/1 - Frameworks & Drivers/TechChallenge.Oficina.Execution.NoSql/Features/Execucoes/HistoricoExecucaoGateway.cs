using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Execution.NoSql.Configuration;
using TechChallenge.Oficina.Execution.NoSql.DynamoDb;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;

namespace TechChallenge.Oficina.Execution.NoSql.Features.Execucoes;

// Linha do tempo da producao no DynamoDB: uma particao por OS (OrdemServicoId) e a chave de ordenacao "OcorridoEm#sufixo"
// (data UTC com milissegundos + id curto), em ordem cronologica e sem colisao entre eventos do mesmo instante. Cada item expira em 180 dias (TTL em ExpiraEm).
// O registro e best-effort: a falha sai como integracao.falha (Warning) e o fluxo da producao segue. A consulta registra a falha e relanca.
internal sealed class HistoricoExecucaoGateway : IHistoricoExecucaoGateway
{
    public const string ChaveParticao = "OrdemServicoId";
    public const string ChaveOrdenacao = "OcorridoEm";
    public const string AtributoExpiracao = "ExpiraEm";
    public const string AtributoTipo = "Tipo";
    public const string AtributoOrigem = "Origem";
    public const string AtributoRepeticao = "Repeticao";
    public const string AtributoTempoNaFilaSegundos = "TempoNaFilaSegundos";
    public const string AtributoTempoExecucaoSegundos = "TempoExecucaoSegundos";

    public static readonly TimeSpan Retencao = TimeSpan.FromDays(180);

    private const string FormatoData = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    private const char SeparadorChaveOrdenacao = '#';
    private const int TamanhoSufixo = 8;

    private readonly IDynamoDbClient _dynamoDbClient;
    private readonly DynamoDbSettings _settings;
    private readonly ILogger<HistoricoExecucaoGateway> _logger;

    public HistoricoExecucaoGateway(IDynamoDbClient dynamoDbClient, DynamoDbSettings settings, ILogger<HistoricoExecucaoGateway> logger)
    {
        _dynamoDbClient = dynamoDbClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task RegistrarAsync(EventoHistoricoExecucao evento, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evento);

        try
        {
            await _dynamoDbClient.PutItemAsync(_settings.TabelaHistoricoExecucoes, CriarItem(evento), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Linha do tempo complementar: a producao ja esta gravada no PostgreSQL, entao a falha nao interrompe o fluxo
            _logger.LogWarning(
                ExecucaoLogEventos.IntegracaoFalha,
                exception,
                "Falha na integracao {Integracao} durante a operacao {Operacao} do evento {Tipo} da ordem de servico {OrdemServicoId}; a linha do tempo da producao fica sem este registro.",
                ExecucaoLogEventos.Integracoes.DynamoDb,
                ExecucaoLogEventos.Operacoes.RegistroHistorico,
                evento.Tipo,
                evento.OrdemServicoId);
        }
    }

    public async Task<IReadOnlyCollection<EventoHistoricoExecucao>> ListarAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var itens = await _dynamoDbClient.QueryAsync(_settings.TabelaHistoricoExecucoes, ChaveParticao, ordemServicoId.ToString("D"), cancellationToken);
            return itens.Select(Mapear).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ExecucaoLogEventos.IntegracaoFalha,
                exception,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da ordem de servico {OrdemServicoId}.",
                ExecucaoLogEventos.Integracoes.DynamoDb,
                ExecucaoLogEventos.Operacoes.ConsultaHistorico,
                ordemServicoId);
            throw;
        }
    }

    // Sem dados pessoais: so o identificador da OS, tipo, origem, repeticao e os tempos
    internal static Dictionary<string, AttributeValue> CriarItem(EventoHistoricoExecucao evento)
    {
        var ocorridoEm = ParaUtc(evento.OcorridoEm);

        var item = new Dictionary<string, AttributeValue>
        {
            [ChaveParticao] = new AttributeValue { S = evento.OrdemServicoId.ToString("D") },
            [ChaveOrdenacao] = new AttributeValue { S = CriarChaveOrdenacao(ocorridoEm) },
            [AtributoExpiracao] = new AttributeValue { N = new DateTimeOffset(ocorridoEm).Add(Retencao).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
            [AtributoTipo] = new AttributeValue { S = evento.Tipo },
            [AtributoOrigem] = new AttributeValue { S = evento.Origem },
            [AtributoRepeticao] = new AttributeValue { BOOL = evento.Repeticao }
        };

        // Atributos opcionais so entram quando preenchidos (tempo na fila no inicio da execucao; tempo de execucao na finalizacao)
        if (evento.TempoNaFilaSegundos is { } tempoNaFilaSegundos)
        {
            item[AtributoTempoNaFilaSegundos] = new AttributeValue { N = FormatarNumero(tempoNaFilaSegundos) };
        }

        if (evento.TempoExecucaoSegundos is { } tempoExecucaoSegundos)
        {
            item[AtributoTempoExecucaoSegundos] = new AttributeValue { N = FormatarNumero(tempoExecucaoSegundos) };
        }

        return item;
    }

    internal static string CriarChaveOrdenacao(DateTime ocorridoEmUtc)
    {
        var sufixo = Guid.NewGuid().ToString("N")[..TamanhoSufixo];
        return $"{ocorridoEmUtc.ToString(FormatoData, CultureInfo.InvariantCulture)}{SeparadorChaveOrdenacao}{sufixo}";
    }

    internal static EventoHistoricoExecucao Mapear(Dictionary<string, AttributeValue> item)
    {
        var data = item[ChaveOrdenacao].S.Split(SeparadorChaveOrdenacao)[0];

        return new EventoHistoricoExecucao(
            Guid.Parse(item[ChaveParticao].S),
            DateTime.ParseExact(data, FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            item[AtributoTipo].S,
            item[AtributoOrigem].S,
            item[AtributoRepeticao].BOOL == true,
            item.TryGetValue(AtributoTempoNaFilaSegundos, out var tempoNaFilaSegundos) ? LerNumero(tempoNaFilaSegundos.N) : null,
            item.TryGetValue(AtributoTempoExecucaoSegundos, out var tempoExecucaoSegundos) ? LerNumero(tempoExecucaoSegundos.N) : null);
    }

    // "R": o menor texto que volta exatamente ao mesmo double (o valor gravado e o mesmo dos logs)
    private static string FormatarNumero(double valor)
    {
        return valor.ToString("R", CultureInfo.InvariantCulture);
    }

    private static double LerNumero(string valor)
    {
        return double.Parse(valor, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // Data sem fuso (Unspecified) e tratada como UTC, como o restante do servico (DateTime.UtcNow)
    private static DateTime ParaUtc(DateTime data)
    {
        return data.Kind == DateTimeKind.Local ? data.ToUniversalTime() : DateTime.SpecifyKind(data, DateTimeKind.Utc);
    }
}
