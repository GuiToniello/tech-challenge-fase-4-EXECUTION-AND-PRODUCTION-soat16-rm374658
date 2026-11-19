using AutoMapper;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Commands;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Queries;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.ViewModels;

namespace TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

// Acoes do mecanico na producao. O status da OS pertence ao OS Service: aqui so se registram as datas e se publicam os eventos que ele consome
// (DiagnosticoIniciado e ExecucaoFinalizada). Toda alteracao e gravada antes da publicacao e, depois de gravar, a publicacao nao e cancelada junto com a
// requisicao (CancellationToken.None). Se a publicacao falhar, a excecao sobe (500) e a nova tentativa do mecanico e uma repeticao que publica de novo.
public sealed class ExecucaoUseCases : IExecucaoUseCases
{
    // Ordem da fila: primeiro o que ja esta sendo executado, depois o que aguarda execucao (pago) e, por ultimo, o diagnostico
    private static readonly Dictionary<EtapaExecucao, int> PrioridadeNaFila = new()
    {
        { EtapaExecucao.EmExecucao, 1 },
        { EtapaExecucao.AguardandoExecucao, 2 },
        { EtapaExecucao.EmDiagnostico, 3 },
        { EtapaExecucao.AguardandoDiagnostico, 4 }
    };

    private readonly IMapper _mapper;
    private readonly IExecucaoGateway _execucaoGateway;
    private readonly IEventosPublisher _eventosPublisher;
    private readonly ILogger<ExecucaoUseCases> _logger;

    public ExecucaoUseCases(
        IMapper mapper,
        IExecucaoGateway execucaoGateway,
        IEventosPublisher eventosPublisher,
        ILogger<ExecucaoUseCases> logger)
    {
        _mapper = mapper;
        _execucaoGateway = execucaoGateway;
        _eventosPublisher = eventosPublisher;
        _logger = logger;
    }

    public async Task<ExecucaoViewModel> ObterPorOrdemServicoIdAsync(ObterExecucaoPorOrdemServicoIdQuery query, CancellationToken cancellationToken = default)
    {
        var execucao = await ObterExecucaoExistenteAsync(query.OrdemServicoId, cancellationToken);
        return _mapper.Map<ExecucaoViewModel>(execucao);
    }

    public async Task<IReadOnlyCollection<ExecucaoViewModel>> ListarFilaAsync(ListarFilaExecucaoQuery query, CancellationToken cancellationToken = default)
    {
        var execucoes = await _execucaoGateway.ListarFilaAsync(cancellationToken);

        // Dentro da mesma etapa, a mais antiga primeiro: entrada na fila de execucao ou, nas etapas de diagnostico, o recebimento da OS
        var fila = execucoes
            .Where(execucao => PrioridadeNaFila.ContainsKey(execucao.Etapa))
            .OrderBy(execucao => PrioridadeNaFila[execucao.Etapa])
            .ThenBy(execucao => execucao.EntrouNaFilaEm ?? execucao.CriadoEm)
            .ToArray();

        return _mapper.Map<IReadOnlyCollection<ExecucaoViewModel>>(fila);
    }

    public async Task<ExecucaoViewModel> IniciarDiagnosticoAsync(IniciarDiagnosticoCommand command, CancellationToken cancellationToken = default)
    {
        var execucao = await ObterExecucaoExistenteAsync(command.OrdemServicoId, cancellationToken);
        var repeticao = execucao.IniciarDiagnostico(DateTime.UtcNow);

        // Repeticao: nada mudou (a data do primeiro pedido e mantida), so o evento e publicado de novo
        if (!repeticao)
        {
            await _execucaoGateway.AtualizarAsync(execucao, cancellationToken);
        }

        _logger.LogInformation(
            ExecucaoLogEventos.ExecucaoDiagnosticoIniciado,
            "Diagnostico da ordem de servico {OrdemServicoId} iniciado (repeticao: {Repeticao}).",
            execucao.OrdemServicoId,
            repeticao);

        await _eventosPublisher.PublicarAsync(new DiagnosticoIniciado(execucao.OrdemServicoId), execucao.OrdemServicoId, CancellationToken.None);

        return _mapper.Map<ExecucaoViewModel>(execucao);
    }

    // Controle interno da oficina: nao publica evento (a OS ja esta EmExecucao no OS Service desde o pagamento aprovado)
    public async Task<ExecucaoViewModel> IniciarExecucaoAsync(IniciarExecucaoCommand command, CancellationToken cancellationToken = default)
    {
        var execucao = await ObterExecucaoExistenteAsync(command.OrdemServicoId, cancellationToken);
        var agora = DateTime.UtcNow;

        execucao.IniciarExecucao(agora);
        await _execucaoGateway.AtualizarAsync(execucao, cancellationToken);

        _logger.LogInformation(
            ExecucaoLogEventos.ExecucaoIniciada,
            "Execucao da ordem de servico {OrdemServicoId} iniciada apos {TempoNaFilaSegundos} segundos na fila.",
            execucao.OrdemServicoId,
            (agora - (execucao.EntrouNaFilaEm ?? execucao.CriadoEm)).TotalSeconds);

        return _mapper.Map<ExecucaoViewModel>(execucao);
    }

    public async Task<ExecucaoViewModel> FinalizarAsync(FinalizarExecucaoCommand command, CancellationToken cancellationToken = default)
    {
        var execucao = await ObterExecucaoExistenteAsync(command.OrdemServicoId, cancellationToken);
        var repeticao = execucao.Finalizar(DateTime.UtcNow);

        // Repeticao: nada mudou (a data da primeira finalizacao e mantida), so o evento e publicado de novo
        if (!repeticao)
        {
            await _execucaoGateway.AtualizarAsync(execucao, cancellationToken);
        }

        _logger.LogInformation(
            ExecucaoLogEventos.ExecucaoFinalizada,
            "Execucao da ordem de servico {OrdemServicoId} finalizada apos {TempoExecucaoSegundos} segundos de execucao (repeticao: {Repeticao}).",
            execucao.OrdemServicoId,
            (execucao.FinalizadaEm!.Value - execucao.ExecucaoIniciadaEm!.Value).TotalSeconds,
            repeticao);

        await _eventosPublisher.PublicarAsync(new ExecucaoFinalizada(execucao.OrdemServicoId), execucao.OrdemServicoId, CancellationToken.None);

        return _mapper.Map<ExecucaoViewModel>(execucao);
    }

    private async Task<Execucao> ObterExecucaoExistenteAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var execucao = await _execucaoGateway.ObterPorIdAsync(ordemServicoId, cancellationToken);

        if (execucao is null)
        {
            throw new KeyNotFoundException("Execucao da ordem de servico nao encontrada.");
        }

        return execucao;
    }
}
