using System.Text.RegularExpressions;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Moq;
using TechChallenge.Oficina.Execution.NoSql.Configuration;
using TechChallenge.Oficina.Execution.NoSql.DynamoDb;
using TechChallenge.Oficina.Execution.NoSql.Features.Execucoes;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using Xunit;

namespace TechChallenge.Oficina.Execution.NoSql.Tests.Features.Execucoes;

public sealed class HistoricoExecucaoGatewayTests
{
    private const string Tabela = "techchallenge-oficina-execution-producao-historico";

    private static readonly DateTime OcorridoEm = new(2026, 11, 24, 10, 15, 30, 123, DateTimeKind.Utc);

    private readonly Mock<IDynamoDbClient> _dynamoDbClientMock = new();
    private readonly Mock<ILogger<HistoricoExecucaoGateway>> _loggerMock = new();
    private readonly Guid _ordemServicoId = Guid.NewGuid();

    private HistoricoExecucaoGateway CriarGateway() =>
        new(_dynamoDbClientMock.Object, new DynamoDbSettings { TabelaHistoricoExecucoes = Tabela }, _loggerMock.Object);

    // Finalizacao repetida (o evento foi publicado de novo), com o tempo de execucao do log execucao.finalizada
    private EventoHistoricoExecucao CriarEvento() => new(
        _ordemServicoId,
        OcorridoEm,
        TiposHistoricoExecucao.ExecucaoFinalizada,
        OrigensHistoricoExecucao.Acao,
        Repeticao: true,
        TempoNaFilaSegundos: null,
        TempoExecucaoSegundos: 5400.125);

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeFalha()
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == ExecucaoLogEventos.IntegracaoFalha.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    [Fact]
    public async Task RegistrarAsync_DeveGravarItemComChavesTtlEAtributosDoContrato()
    {
        Dictionary<string, AttributeValue>? gravado = null;
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(Tabela, It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, AttributeValue>, CancellationToken>((_, item, _) => gravado = item)
            .Returns(Task.CompletedTask);

        await CriarGateway().RegistrarAsync(CriarEvento());

        Assert.NotNull(gravado);
        Assert.Equal(_ordemServicoId.ToString("D"), gravado["OrdemServicoId"].S);
        Assert.Matches(new Regex("^2026-11-24T10:15:30\\.123Z#[0-9a-f]{8}$"), gravado["OcorridoEm"].S);
        Assert.Equal(new DateTimeOffset(OcorridoEm).AddDays(180).ToUnixTimeSeconds().ToString(), gravado["ExpiraEm"].N);
        Assert.Equal("ExecucaoFinalizada", gravado["Tipo"].S);
        Assert.Equal("acao", gravado["Origem"].S);
        Assert.True(gravado["Repeticao"].BOOL);
        Assert.Equal("5400.125", gravado["TempoExecucaoSegundos"].N);
        Assert.DoesNotContain("TempoNaFilaSegundos", gravado.Keys);
        Assert.Equal(7, gravado.Count);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public void CriarItem_NaoDeveIncluirOsTempos_QuandoNaoInformados()
    {
        var entrada = CriarEvento() with
        {
            Tipo = TiposHistoricoExecucao.EntrouNaFila,
            Origem = OrigensHistoricoExecucao.Evento,
            Repeticao = false,
            TempoExecucaoSegundos = null
        };

        var item = HistoricoExecucaoGateway.CriarItem(entrada);

        Assert.DoesNotContain("TempoNaFilaSegundos", item.Keys);
        Assert.DoesNotContain("TempoExecucaoSegundos", item.Keys);
        Assert.False(item["Repeticao"].BOOL);
        Assert.Equal("evento", item["Origem"].S);
        Assert.Equal(6, item.Count);
    }

    [Fact]
    public void CriarChaveOrdenacao_DeveSerUnicaNoMesmoInstanteEOrdenarCronologicamente()
    {
        var primeira = HistoricoExecucaoGateway.CriarChaveOrdenacao(OcorridoEm);
        var mesmoInstante = HistoricoExecucaoGateway.CriarChaveOrdenacao(OcorridoEm);
        var seguinte = HistoricoExecucaoGateway.CriarChaveOrdenacao(OcorridoEm.AddMilliseconds(1));

        Assert.NotEqual(primeira, mesmoInstante);
        Assert.True(string.CompareOrdinal(primeira, seguinte) < 0);
        Assert.True(string.CompareOrdinal(mesmoInstante, seguinte) < 0);
    }

    [Fact]
    public void Mapear_DeveRecuperarOEventoGravado()
    {
        // Inclui um tempo sem representacao exata em binario (0.1 + 0.2): o valor lido e exatamente o gravado, o mesmo dos logs
        EventoHistoricoExecucao[] eventos =
        [
            CriarEvento(),
            CriarEvento() with { Tipo = TiposHistoricoExecucao.ExecucaoIniciada, Repeticao = false, TempoNaFilaSegundos = 0.1 + 0.2, TempoExecucaoSegundos = null },
            CriarEvento() with { Tipo = TiposHistoricoExecucao.DiagnosticoIniciado, Repeticao = false, TempoExecucaoSegundos = null }
        ];

        foreach (var evento in eventos)
        {
            var mapeado = HistoricoExecucaoGateway.Mapear(HistoricoExecucaoGateway.CriarItem(evento));

            Assert.Equal(evento, mapeado);
            Assert.Equal(DateTimeKind.Utc, mapeado.OcorridoEm.Kind);
        }
    }

    [Fact]
    public async Task RegistrarAsync_DeveRegistrarWarningSemLancar_QuandoDynamoDbFalha()
    {
        var falha = new InvalidOperationException("DynamoDB indisponivel");
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        await CriarGateway().RegistrarAsync(CriarEvento());

        var (nivel, propriedades, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("dynamodb", propriedades["Integracao"]);
        Assert.Equal("registro_historico", propriedades["Operacao"]);
        Assert.Equal("ExecucaoFinalizada", propriedades["Tipo"]);
        Assert.Equal(_ordemServicoId, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task RegistrarAsync_DeveRegistrarWarningSemLancar_QuandoTempoEsgotadoSemCancelarARequisicao()
    {
        // Os casos de uso registram com CancellationToken.None: o tempo esgotado do SDK (TaskCanceledException) tambem e so um aviso
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Tempo esgotado"));

        await CriarGateway().RegistrarAsync(CriarEvento(), CancellationToken.None);

        var (nivel, _, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.IsType<TaskCanceledException>(excecao);
    }

    [Fact]
    public async Task RegistrarAsync_DevePropagarCancelamentoDaRequisicao()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await CriarGateway().RegistrarAsync(CriarEvento(), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public async Task ListarAsync_DeveConsultarAParticaoDaOrdemEMapearOsItens()
    {
        var entrada = CriarEvento() with { Tipo = TiposHistoricoExecucao.EntrouNaFila, Origem = OrigensHistoricoExecucao.Evento, Repeticao = false, TempoExecucaoSegundos = null };
        var inicio = CriarEvento() with
        {
            OcorridoEm = OcorridoEm.AddMinutes(2),
            Tipo = TiposHistoricoExecucao.ExecucaoIniciada,
            Repeticao = false,
            TempoNaFilaSegundos = 120,
            TempoExecucaoSegundos = null
        };
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(Tabela, "OrdemServicoId", _ordemServicoId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([HistoricoExecucaoGateway.CriarItem(entrada), HistoricoExecucaoGateway.CriarItem(inicio)]);

        var historico = await CriarGateway().ListarAsync(_ordemServicoId);

        Assert.Equal([entrada, inicio], historico);
    }

    [Fact]
    public async Task ListarAsync_DeveRegistrarErroERelancar_QuandoDynamoDbFalha()
    {
        var falha = new InvalidOperationException("DynamoDB indisponivel");
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await CriarGateway().ListarAsync(_ordemServicoId);

        Assert.Same(falha, await Assert.ThrowsAsync<InvalidOperationException>(action));
        var (nivel, propriedades, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("dynamodb", propriedades["Integracao"]);
        Assert.Equal("consulta_historico", propriedades["Operacao"]);
        Assert.Equal(_ordemServicoId, propriedades["OrdemServicoId"]);
    }
}
