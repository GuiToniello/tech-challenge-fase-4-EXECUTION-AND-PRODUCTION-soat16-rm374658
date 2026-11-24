namespace TechChallenge.Oficina.Execution.NoSql.Configuration;

public sealed class DynamoDbSettings
{
    public const string SectionName = "DynamoDbSettings";

    // Tabela da linha do tempo da producao (criada pelo Terraform na AWS; no ambiente local, pela propria API)
    public string TabelaHistoricoExecucoes { get; set; } = string.Empty;

    // Endpoint do DynamoDB Local. Vazio na AWS: regiao e credenciais vem do ambiente.
    public string ServiceUrl { get; set; } = string.Empty;
}
