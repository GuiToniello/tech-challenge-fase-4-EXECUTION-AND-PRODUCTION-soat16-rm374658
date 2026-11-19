using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Execution.DB.Data.Context;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

namespace TechChallenge.Oficina.Execution.DB.Data.Features.Execucoes;

public sealed class ExecucaoGateway : IExecucaoGateway
{
    private readonly ExecucaoDbContext _context;

    public ExecucaoGateway(ExecucaoDbContext context)
    {
        _context = context;
    }

    public async Task AdicionarAsync(Execucao execucao, CancellationToken cancellationToken = default)
    {
        await _context.Execucoes.AddAsync(execucao, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AtualizarAsync(Execucao execucao, CancellationToken cancellationToken = default)
    {
        _context.Execucoes.Update(execucao);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Execucao?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        return await _context.Execucoes
            .FirstOrDefaultAsync(execucao => execucao.Id == ordemServicoId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Execucao>> ListarFilaAsync(CancellationToken cancellationToken = default)
    {
        // Etapas da fila (AguardandoDiagnostico, EmDiagnostico, AguardandoExecucao e EmExecucao) pelas colunas: a ordem e definida no caso de uso
        return await _context.Execucoes
            .Where(execucao => execucao.RemovidaEm == null &&
                               execucao.FinalizadaEm == null &&
                               (execucao.StatusOrdemServico == StatusOrdemServico.Recebida ||
                                execucao.StatusOrdemServico == StatusOrdemServico.EmDiagnostico ||
                                execucao.StatusOrdemServico == StatusOrdemServico.EmExecucao))
            .ToArrayAsync(cancellationToken);
    }
}
