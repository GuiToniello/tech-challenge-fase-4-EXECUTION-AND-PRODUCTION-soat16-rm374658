using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TechChallenge.Oficina.Execution.DB.Data.Context;
using TechChallenge.Oficina.Execution.DB.Data.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Execution.DB.Data.Tests;

public sealed class ExtensionsTests
{
    private const string ConnectionString = "Host=localhost;Port=5435;Database=oficina_execution;Username=sa;Password=senha";

    [Fact]
    public void AddInfraData_DeveRegistrarGatewayEHealthCheckDoBanco()
    {
        var services = new ServiceCollection();

        services.AddInfraData(ConnectionString);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<ExecucaoGateway>(scope.ServiceProvider.GetRequiredService<IExecucaoGateway>());

        var healthChecks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var postgres = Assert.Single(healthChecks, registro => registro.Name == "postgres");
        Assert.Contains("ready", postgres.Tags);
    }

    [Fact]
    public void Modelo_DeveMapearTabelaEColunasNoPostgres()
    {
        var options = new DbContextOptionsBuilder<ExecucaoDbContext>().UseNpgsql(ConnectionString).Options;
        using var context = new ExecucaoDbContext(options);

        var execucao = context.Model.FindEntityType(typeof(Execucao))!;
        Assert.Equal("execucoes", execucao.GetTableName());
        Assert.Equal("id", execucao.FindProperty(nameof(Execucao.Id))!.GetColumnName());
        Assert.Equal("cliente_id", execucao.FindProperty(nameof(Execucao.ClienteId))!.GetColumnName());
        Assert.Equal("veiculo_id", execucao.FindProperty(nameof(Execucao.VeiculoId))!.GetColumnName());
        Assert.Equal("status", execucao.FindProperty(nameof(Execucao.StatusOrdemServico))!.GetColumnName());
        Assert.Equal("integer", execucao.FindProperty(nameof(Execucao.StatusOrdemServico))!.GetColumnType());
        Assert.Equal("criado_em", execucao.FindProperty(nameof(Execucao.CriadoEm))!.GetColumnName());
        Assert.Equal("diagnostico_iniciado_em", execucao.FindProperty(nameof(Execucao.DiagnosticoIniciadoEm))!.GetColumnName());
        Assert.Equal("entrou_na_fila_em", execucao.FindProperty(nameof(Execucao.EntrouNaFilaEm))!.GetColumnName());
        Assert.Equal("execucao_iniciada_em", execucao.FindProperty(nameof(Execucao.ExecucaoIniciadaEm))!.GetColumnName());
        Assert.Equal("finalizada_em", execucao.FindProperty(nameof(Execucao.FinalizadaEm))!.GetColumnName());
        Assert.Equal("removida_em", execucao.FindProperty(nameof(Execucao.RemovidaEm))!.GetColumnName());

        // Nomes dos servicos numa coluna text[] (primitive collection)
        var servicos = execucao.FindProperty(nameof(Execucao.Servicos))!;
        Assert.True(servicos.IsPrimitiveCollection);
        Assert.Equal("servicos", servicos.GetColumnName());
        Assert.Equal("text[]", servicos.GetColumnType());
        Assert.False(servicos.IsNullable);

        // Concorrencia otimista pela coluna de sistema xmin
        var versao = execucao.FindProperty(nameof(Execucao.Versao))!;
        Assert.Equal("xmin", versao.GetColumnName());
        Assert.Equal("xid", versao.GetColumnType());
        Assert.True(versao.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, versao.ValueGenerated);

        // Etapa e calculada e OrdemServicoId e o proprio Id: nenhuma das duas vira coluna
        Assert.Null(execucao.FindProperty(nameof(Execucao.Etapa)));
        Assert.Null(execucao.FindProperty(nameof(Execucao.OrdemServicoId)));
        Assert.Contains(execucao.GetIndexes(), indice => indice.Properties.Single().Name == nameof(Execucao.StatusOrdemServico));
    }
}
