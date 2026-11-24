using TechChallenge.Oficina.Execution.Adapters.Features.Execucoes;
using TechChallenge.Oficina.Execution.API.Extensions;
using TechChallenge.Oficina.Execution.API.Features.Execucoes;
using TechChallenge.Oficina.Execution.API.Middleware;
using TechChallenge.Oficina.Execution.API.Settings;
using TechChallenge.Oficina.Execution.Controllers.Features.Eventos;
using TechChallenge.Oficina.Execution.Controllers.Features.Execucoes;
using TechChallenge.Oficina.Execution.DB.Data;
using TechChallenge.Oficina.Execution.Messaging;
using TechChallenge.Oficina.Execution.Messaging.Configuration;
using TechChallenge.Oficina.Execution.NoSql;
using TechChallenge.Oficina.Execution.NoSql.Configuration;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.UseCases;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Mappings;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddLoggingConfiguration(builder.Configuration);
builder.Services.AddSwaggerConfiguration();
builder.Services.AddAuthConfiguration(builder.Configuration);

var databaseSettings = builder.Configuration
    .GetSection(DatabaseSettings.SectionName)
    .Get<DatabaseSettings>()
    ?? throw new InvalidOperationException("A configuração de banco de dados é obrigatória.");

if (string.IsNullOrWhiteSpace(databaseSettings.ConnectionString))
{
    throw new InvalidOperationException("A connection string do banco de dados é obrigatória.");
}

// Integracao com a Management e o OS Service por eventos (Amazon SQS); credenciais e regiao pela cadeia padrao do AWS SDK
var sqsSettings = builder.Configuration
    .GetSection(SqsSettings.SectionName)
    .Get<SqsSettings>()
    ?? new SqsSettings();

if (sqsSettings.FilasPublicacao.Count == 0 || sqsSettings.FilasPublicacao.Any(string.IsNullOrWhiteSpace))
{
    throw new InvalidOperationException("SqsSettings:FilasPublicacao é obrigatório (fila do OS Service, que consome DiagnosticoIniciado e ExecucaoFinalizada).");
}

// Linha do tempo da producao no Amazon DynamoDB: complementar e best-effort (a fonte da verdade e o PostgreSQL); credenciais e regiao pela cadeia padrao do AWS SDK
var dynamoDbSettings = builder.Configuration
    .GetSection(DynamoDbSettings.SectionName)
    .Get<DynamoDbSettings>()
    ?? new DynamoDbSettings();

builder.Services.AddAutoMapper(_ => { }, typeof(ExecucaoProfile).Assembly);
builder.Services.AddScoped<IExecucaoUseCases, ExecucaoUseCases>();
builder.Services.AddScoped<IExecucaoEventosUseCases, ExecucaoEventosUseCases>();
builder.Services.AddInfraData(databaseSettings.ConnectionString);
builder.Services.AddInfraMessaging(sqsSettings);
builder.Services.AddInfraNoSql(dynamoDbSettings);

builder.Services.AddScoped<IExecucoesController, ExecucoesController>();
builder.Services.AddScoped<IExecucoesAdapter, ExecucoesAdapter>();
builder.Services.AddScoped<IEventosController, EventosController>();

var app = builder.Build();

try
{
    app.Services.ApplyMigrations();
}
catch (Exception ex)
{
    app.Logger.LogCritical(new EventId(9002, "app.migracao_falhou"), ex, "Falha ao aplicar as migrations do banco");
    Environment.ExitCode = 1;
    return;
}

// Local (DynamoDB Local): cria a tabela da linha do tempo; na AWS a tabela e do Terraform. Falha so gera aviso.
await app.Services.GarantirTabelasLocaisAsync();

app.UseLoggingConfiguration();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseSwaggerConfiguration();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapExecucaoEndpoints();

await app.RunAsync();
