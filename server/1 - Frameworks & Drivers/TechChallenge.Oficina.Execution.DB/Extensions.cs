using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechChallenge.Oficina.Execution.DB.Data.Context;
using TechChallenge.Oficina.Execution.DB.Data.Features.Execucoes;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

namespace TechChallenge.Oficina.Execution.DB.Data;

public static class Extensions
{
    public static IServiceCollection AddInfraData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ExecucaoDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHealthChecks().AddDbContextCheck<ExecucaoDbContext>("postgres", tags: ["ready"]);
        services.AddScoped<IExecucaoGateway, ExecucaoGateway>();
        return services;
    }

    public static void ApplyMigrations(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExecucaoDbContext>();
        dbContext.Database.Migrate();
    }
}
