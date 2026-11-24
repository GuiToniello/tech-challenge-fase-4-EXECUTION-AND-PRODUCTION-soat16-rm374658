using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Interfaces;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.Logging;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;

namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos.UseCases;

// Mantem o item da producao a partir dos eventos da Management e do OS Service (saga coreografada). E idempotente: a fila entrega pelo menos uma vez,
// entao evento repetido resulta em Ignorado. Sem ordem garantida entre os dois publicadores, evento de OS desconhecida sempre cria o registro:
// parcial pelo status (inclusive os finais) e tombstone pela exclusao, para que uma validacao atrasada nao coloque a OS na fila.
// Dados invalidos lancam excecao: a mensagem volta para a fila e, depois de 5 recebimentos, vai para a DLQ.
public sealed class ExecucaoEventosUseCases : IExecucaoEventosUseCases
{
    private readonly IExecucaoGateway _execucaoGateway;
    private readonly IHistoricoExecucaoGateway _historicoExecucaoGateway;
    private readonly ILogger<ExecucaoEventosUseCases> _logger;

    public ExecucaoEventosUseCases(
        IExecucaoGateway execucaoGateway,
        IHistoricoExecucaoGateway historicoExecucaoGateway,
        ILogger<ExecucaoEventosUseCases> logger)
    {
        _execucaoGateway = execucaoGateway;
        _historicoExecucaoGateway = historicoExecucaoGateway;
        _logger = logger;
    }

    // Grava ou atualiza os dados; nunca mexe no status nem nas datas (um item excluido ou encerrado continua fora da fila)
    public async Task<ResultadoProcessamentoEvento> ProcessarValidadaAsync(OrdemServicoValidada evento, CancellationToken cancellationToken = default)
    {
        var servicos = (evento.Servicos ?? [])
            .Select(servico => servico?.Nome ?? string.Empty)
            .ToArray();

        var execucao = await _execucaoGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (execucao is null)
        {
            execucao = Execucao.CriarPorValidacao(evento.OrdemServicoId, evento.ClienteId, evento.VeiculoId, servicos, DateTime.UtcNow);
            await _execucaoGateway.AdicionarAsync(execucao, cancellationToken);
        }
        else
        {
            execucao.AtualizarDados(evento.ClienteId, evento.VeiculoId, servicos);
            await _execucaoGateway.AtualizarAsync(execucao, cancellationToken);
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    // O status pode chegar antes do OrdemServicoValidada ou ser o unico evento da OS (ex.: cancelada pela Management): cria o item parcial
    public async Task<ResultadoProcessamentoEvento> ProcessarStatusAlteradoAsync(OrdemServicoStatusAlterado evento, CancellationToken cancellationToken = default)
    {
        var statusNovo = (StatusOrdemServico)evento.StatusNovo;
        var execucao = await _execucaoGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (execucao is null)
        {
            execucao = Execucao.CriarParcial(evento.OrdemServicoId, evento.ClienteId, statusNovo, DateTime.UtcNow);
            await _execucaoGateway.AdicionarAsync(execucao, cancellationToken);
            await RegistrarEntradaNaFilaAsync(execucao, entrouNaFilaAntes: null);
            return ResultadoProcessamentoEvento.Processado;
        }

        if (execucao.StatusOrdemServico == statusNovo)
        {
            _logger.LogInformation(
                "Evento OrdemServicoStatusAlterado ignorado: a ordem de servico {OrdemServicoId} ja esta no status {StatusNovo}.",
                execucao.OrdemServicoId,
                ExecucaoLogEventos.StatusParaLog(statusNovo));
            return ResultadoProcessamentoEvento.Ignorado;
        }

        var entrouNaFilaAntes = execucao.EntrouNaFilaEm;
        execucao.AtualizarStatus(statusNovo, DateTime.UtcNow);
        await _execucaoGateway.AtualizarAsync(execucao, cancellationToken);
        await RegistrarEntradaNaFilaAsync(execucao, entrouNaFilaAntes);

        return ResultadoProcessamentoEvento.Processado;
    }

    // A OS sai da producao, mas o registro fica (tombstone), inclusive quando a exclusao e o primeiro evento recebido da OS
    public async Task<ResultadoProcessamentoEvento> ProcessarExcluidaAsync(OrdemServicoExcluida evento, CancellationToken cancellationToken = default)
    {
        var execucao = await _execucaoGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (execucao is null)
        {
            await _execucaoGateway.AdicionarAsync(Execucao.CriarRemovida(evento.OrdemServicoId, DateTime.UtcNow), cancellationToken);
            return ResultadoProcessamentoEvento.Processado;
        }

        if (execucao.RemovidaEm is not null)
        {
            _logger.LogInformation("Evento OrdemServicoExcluida ignorado: a ordem de servico {OrdemServicoId} ja saiu da producao.", execucao.OrdemServicoId);
            return ResultadoProcessamentoEvento.Ignorado;
        }

        execucao.Remover(DateTime.UtcNow);
        await _execucaoGateway.AtualizarAsync(execucao, cancellationToken);

        return ResultadoProcessamentoEvento.Processado;
    }

    // Linha do tempo da producao (DynamoDB), so quando a OS acaba de entrar na fila de execucao (EntrouNaFilaEm de vazio para preenchido).
    // A data da primeira entrada e mantida, entao uma nova passagem por EmExecucao nao gera outro registro.
    // Best-effort (o gateway nao lanca) e sem o token do consumidor: chamado so depois da gravacao.
    private async Task RegistrarEntradaNaFilaAsync(Execucao execucao, DateTime? entrouNaFilaAntes)
    {
        if (entrouNaFilaAntes is not null || execucao.EntrouNaFilaEm is not { } entrouNaFilaEm)
        {
            return;
        }

        await _historicoExecucaoGateway.RegistrarAsync(
            new EventoHistoricoExecucao(
                execucao.OrdemServicoId,
                entrouNaFilaEm,
                TiposHistoricoExecucao.EntrouNaFila,
                OrigensHistoricoExecucao.Evento,
                Repeticao: false,
                TempoNaFilaSegundos: null,
                TempoExecucaoSegundos: null),
            CancellationToken.None);
    }
}
