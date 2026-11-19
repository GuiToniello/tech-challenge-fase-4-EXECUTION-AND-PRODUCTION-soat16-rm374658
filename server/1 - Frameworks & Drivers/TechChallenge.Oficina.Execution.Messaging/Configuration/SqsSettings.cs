namespace TechChallenge.Oficina.Execution.Messaging.Configuration;

public sealed class SqsSettings
{
    public const string SectionName = "SqsSettings";

    // Fila FIFO de onde o Execution consome os eventos da Management e do OS Service. Vazia: o consumidor nao e iniciado.
    public string FilaConsumo { get; set; } = string.Empty;

    // Filas FIFO de destino dos eventos do Execution (hoje so a do OS Service): o mesmo envelope de cada evento vai para todas.
    // Por variavel de ambiente: SqsSettings__FilasPublicacao__0, SqsSettings__FilasPublicacao__1...
    public List<string> FilasPublicacao { get; set; } = [];

    // Endpoint do emulador local (ElasticMQ). Vazio na AWS: regiao e credenciais vem do ambiente.
    public string ServiceUrl { get; set; } = string.Empty;
}
