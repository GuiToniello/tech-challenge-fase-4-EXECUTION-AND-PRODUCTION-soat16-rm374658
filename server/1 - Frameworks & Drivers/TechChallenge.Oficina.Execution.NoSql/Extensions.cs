using Amazon.DynamoDBv2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Execution.NoSql.Configuration;
using TechChallenge.Oficina.Execution.NoSql.DynamoDb;
using TechChallenge.Oficina.Execution.NoSql.Features.Execucoes;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;

namespace TechChallenge.Oficina.Execution.NoSql;

public static class Extensions
{
    public static IServiceCollection AddInfraNoSql(this IServiceCollection services, DynamoDbSettings dynamoDbSettings)
    {
        services.AddSingleton(dynamoDbSettings);
        services.AddSingleton<IAmazonDynamoDB>(_ => CriarClienteDynamoDb(dynamoDbSettings));
        services.AddSingleton<IDynamoDbClient, DynamoDbClientAdapter>();
        services.AddSingleton<IHistoricoExecucaoGateway, HistoricoExecucaoGateway>();

        return services;
    }

    // So no ambiente local (ServiceUrl preenchido): cria a tabela da linha do tempo no DynamoDB Local com as mesmas chaves e TTL da AWS.
    // Na AWS a tabela e do Terraform e o IAM do Execution so permite PutItem e Query. Uma falha aqui nao impede a API de subir.
    public static async Task GarantirTabelasLocaisAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var settings = serviceProvider.GetRequiredService<DynamoDbSettings>();

        if (string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            return;
        }

        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Extensions));

        try
        {
            var criada = await serviceProvider.GetRequiredService<IDynamoDbClient>().CriarTabelaSeNaoExisteAsync(
                settings.TabelaHistoricoExecucoes,
                HistoricoExecucaoGateway.ChaveParticao,
                HistoricoExecucaoGateway.ChaveOrdenacao,
                HistoricoExecucaoGateway.AtributoExpiracao,
                cancellationToken);

            logger.LogInformation(
                "Tabela {Tabela} do DynamoDB local {Situacao} em {ServiceUrl}.",
                settings.TabelaHistoricoExecucoes,
                criada ? "criada" : "ja existente",
                settings.ServiceUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Falha ao criar a tabela {Tabela} no DynamoDB local em {ServiceUrl}; a linha do tempo da producao fica indisponivel, mas a API segue.",
                settings.TabelaHistoricoExecucoes,
                settings.ServiceUrl);
        }
    }

    private static AmazonDynamoDBClient CriarClienteDynamoDb(DynamoDbSettings dynamoDbSettings)
    {
        // Local: ServiceUrl aponta para o DynamoDB Local (credenciais ficticias via AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY).
        // Na AWS fica vazio: regiao (AWS_REGION) e credenciais vem do ambiente do pod.
        if (!string.IsNullOrWhiteSpace(dynamoDbSettings.ServiceUrl))
        {
            return new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = dynamoDbSettings.ServiceUrl });
        }

        return new AmazonDynamoDBClient();
    }
}
