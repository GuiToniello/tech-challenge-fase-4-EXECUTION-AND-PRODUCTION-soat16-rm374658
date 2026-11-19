using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;

namespace TechChallenge.Oficina.Execution.DB.Data.Context;

public sealed class ExecucaoDbContext : DbContext
{
    public ExecucaoDbContext(DbContextOptions<ExecucaoDbContext> options) : base(options)
    {
    }

    public DbSet<Execucao> Execucoes => Set<Execucao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExecucaoDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
