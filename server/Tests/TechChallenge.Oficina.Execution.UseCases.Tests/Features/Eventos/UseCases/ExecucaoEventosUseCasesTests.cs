using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Execution.UseCases.Features.Eventos.UseCases;
using TechChallenge.Oficina.Execution.UseCases.Features.Execucoes.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Execution.UseCases.Tests.Features.Eventos.UseCases;

public sealed class ExecucaoEventosUseCasesTests
{
    private static readonly DateTime Inicio = new(2026, 11, 19, 8, 00, 00, DateTimeKind.Utc);

    private readonly Mock<IExecucaoGateway> _gatewayMock = new();
    private readonly Dictionary<Guid, Execucao> _gravadas = [];
    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly Guid _veiculoId = Guid.NewGuid();

    // O gateway guarda o que foi adicionado: eventos em sequencia enxergam o resultado dos anteriores, como no banco
    public ExecucaoEventosUseCasesTests()
    {
        _gatewayMock
            .Setup(g => g.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _gravadas.GetValueOrDefault(id));
        _gatewayMock
            .Setup(g => g.AdicionarAsync(It.IsAny<Execucao>(), It.IsAny<CancellationToken>()))
            .Callback<Execucao, CancellationToken>((execucao, _) => _gravadas.Add(execucao.Id, execucao))
            .Returns(Task.CompletedTask);
    }

    private ExecucaoEventosUseCases CriarUseCases() => new(_gatewayMock.Object, NullLogger<ExecucaoEventosUseCases>.Instance);

    private OrdemServicoValidada CriarValidada(params string[] nomesServicos) =>
        new(
            _ordemServicoId,
            _clienteId,
            _veiculoId,
            "cliente@teste.com",
            true,
            (nomesServicos.Length > 0 ? nomesServicos : ["Troca de oleo"]).Select(nome => new ServicoValidado(Guid.NewGuid(), nome, 100m)).ToArray(),
            100m);

    private OrdemServicoStatusAlterado CriarStatusAlterado(StatusOrdemServico anterior, StatusOrdemServico novo) =>
        new(_ordemServicoId, _clienteId, (int)anterior, (int)novo);

    private Execucao Gravar(Execucao execucao)
    {
        _gravadas.Add(execucao.Id, execucao);
        return execucao;
    }

    private Execucao ObterGravada() => _gravadas[_ordemServicoId];

    private void VerificarSemGravacao()
    {
        _gatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Execucao>(), It.IsAny<CancellationToken>()), Times.Never);
        _gatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Execucao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveCriarItemAguardandoDiagnostico_QuandoOrdemDesconhecida()
    {
        var resultado = await CriarUseCases().ProcessarValidadaAsync(CriarValidada("Troca de oleo", "Alinhamento"));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        var execucao = ObterGravada();
        Assert.Equal(_clienteId, execucao.ClienteId);
        Assert.Equal(_veiculoId, execucao.VeiculoId);
        Assert.Equal(["Troca de oleo", "Alinhamento"], execucao.Servicos);
        Assert.Equal(StatusOrdemServico.Recebida, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.AguardandoDiagnostico, execucao.Etapa);
        _gatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Execucao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveAtualizarSoOsDados_QuandoItemJaExiste()
    {
        var existente = Gravar(Execucao.CriarParcial(_ordemServicoId, _clienteId, StatusOrdemServico.EmExecucao, Inicio));

        var resultado = await CriarUseCases().ProcessarValidadaAsync(CriarValidada("Balanceamento"));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(_veiculoId, existente.VeiculoId);
        Assert.Equal(["Balanceamento"], existente.Servicos);
        Assert.Equal(StatusOrdemServico.EmExecucao, existente.StatusOrdemServico);
        Assert.Equal(Inicio, existente.EntrouNaFilaEm);
        Assert.Equal(EtapaExecucao.AguardandoExecucao, existente.Etapa);
        _gatewayMock.Verify(g => g.AtualizarAsync(existente, It.IsAny<CancellationToken>()), Times.Once);
        _gatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Execucao>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_NuncaDeveMexerNoStatusNemNasDatas()
    {
        var existente = Execucao.CriarPorValidacao(_ordemServicoId, _clienteId, _veiculoId, ["Troca de oleo"], Inicio);
        existente.IniciarDiagnostico(Inicio.AddMinutes(5));
        existente.AtualizarStatus(StatusOrdemServico.AguardandoAprovacao, Inicio.AddMinutes(10));
        Gravar(existente);

        await CriarUseCases().ProcessarValidadaAsync(CriarValidada("Alinhamento"));

        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, existente.StatusOrdemServico);
        Assert.Equal(Inicio.AddMinutes(5), existente.DiagnosticoIniciadoEm);
        Assert.Equal(Inicio, existente.CriadoEm);
        Assert.Equal(EtapaExecucao.AguardandoAprovacao, existente.Etapa);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveLancarSemGravar_QuandoServicoSemNome()
    {
        var action = async () => await CriarUseCases().ProcessarValidadaAsync(CriarValidada("Troca de oleo", "   "));

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Todos os servicos informados devem possuir nome.", exception.Message);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveLancarSemGravar_QuandoSemServicos()
    {
        var evento = new OrdemServicoValidada(_ordemServicoId, _clienteId, _veiculoId, null, true, null!, 0m);

        var action = async () => await CriarUseCases().ProcessarValidadaAsync(evento);

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("A ordem de servico deve possuir ao menos um servico.", exception.Message);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveLancarSemGravar_QuandoOrdemServicoIdVazio()
    {
        var evento = CriarValidada() with { OrdemServicoId = Guid.Empty };

        var action = async () => await CriarUseCases().ProcessarValidadaAsync(evento);

        await Assert.ThrowsAsync<DomainException>(action);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveCriarItemParcial_QuandoChegaAntesDaValidacao()
    {
        var resultado = await CriarUseCases().ProcessarStatusAlteradoAsync(CriarStatusAlterado(StatusOrdemServico.Recebida, StatusOrdemServico.EmDiagnostico));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        var execucao = ObterGravada();
        Assert.Equal(_clienteId, execucao.ClienteId);
        Assert.Equal(Guid.Empty, execucao.VeiculoId);
        Assert.Empty(execucao.Servicos);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.EmDiagnostico, execucao.Etapa);
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveCriarItemParcialNaFilaDeExecucao_QuandoStatusEmExecucao()
    {
        var antes = DateTime.UtcNow;

        await CriarUseCases().ProcessarStatusAlteradoAsync(CriarStatusAlterado(StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao));

        var execucao = ObterGravada();
        Assert.NotNull(execucao.EntrouNaFilaEm);
        Assert.InRange(execucao.EntrouNaFilaEm!.Value, antes, DateTime.UtcNow);
        Assert.Equal(EtapaExecucao.AguardandoExecucao, execucao.Etapa);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Encerrada)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public async Task ProcessarStatusAlteradoAsync_DeveGravarRegistroTambemParaStatusFinal(StatusOrdemServico status)
    {
        var resultado = await CriarUseCases().ProcessarStatusAlteradoAsync(CriarStatusAlterado(StatusOrdemServico.Recebida, status));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(status, ObterGravada().StatusOrdemServico);
        Assert.Equal(EtapaExecucao.ForaDaProducao, ObterGravada().Etapa);
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveIgnorarSemGravar_QuandoStatusRepetido()
    {
        Gravar(Execucao.CriarParcial(_ordemServicoId, _clienteId, StatusOrdemServico.EmDiagnostico, Inicio));

        var resultado = await CriarUseCases().ProcessarStatusAlteradoAsync(CriarStatusAlterado(StatusOrdemServico.Recebida, StatusOrdemServico.EmDiagnostico));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveAtualizarStatusERegistrarEntradaNaFila()
    {
        var existente = Gravar(Execucao.CriarPorValidacao(_ordemServicoId, _clienteId, _veiculoId, ["Troca de oleo"], Inicio));
        existente.AtualizarStatus(StatusOrdemServico.AguardandoAprovacao, Inicio.AddMinutes(10));
        var antes = DateTime.UtcNow;

        var resultado = await CriarUseCases().ProcessarStatusAlteradoAsync(CriarStatusAlterado(StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(StatusOrdemServico.EmExecucao, existente.StatusOrdemServico);
        Assert.InRange(existente.EntrouNaFilaEm!.Value, antes, DateTime.UtcNow);
        Assert.Equal(EtapaExecucao.AguardandoExecucao, existente.Etapa);
        _gatewayMock.Verify(g => g.AtualizarAsync(existente, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveLancarSemGravar_QuandoStatusInvalido()
    {
        Gravar(Execucao.CriarPorValidacao(_ordemServicoId, _clienteId, _veiculoId, ["Troca de oleo"], Inicio));

        var action = async () => await CriarUseCases().ProcessarStatusAlteradoAsync(new OrdemServicoStatusAlterado(_ordemServicoId, _clienteId, 1, 99));

        await Assert.ThrowsAsync<DomainException>(action);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveLancarSemGravar_QuandoOrdemDesconhecidaComStatusInvalido()
    {
        var action = async () => await CriarUseCases().ProcessarStatusAlteradoAsync(new OrdemServicoStatusAlterado(_ordemServicoId, _clienteId, 0, 0));

        await Assert.ThrowsAsync<DomainException>(action);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarExcluidaAsync_DeveGravarTombstone_QuandoOrdemDesconhecida()
    {
        var resultado = await CriarUseCases().ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.NotNull(ObterGravada().RemovidaEm);
        Assert.Equal(EtapaExecucao.ForaDaProducao, ObterGravada().Etapa);
    }

    [Fact]
    public async Task ProcessarExcluidaAsync_DeveTirarItemDaProducao()
    {
        var existente = Gravar(Execucao.CriarPorValidacao(_ordemServicoId, _clienteId, _veiculoId, ["Troca de oleo"], Inicio));

        var resultado = await CriarUseCases().ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.NotNull(existente.RemovidaEm);
        Assert.Equal(EtapaExecucao.ForaDaProducao, existente.Etapa);
        _gatewayMock.Verify(g => g.AtualizarAsync(existente, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarExcluidaAsync_DeveIgnorarSemGravar_QuandoJaRemovida()
    {
        Gravar(Execucao.CriarRemovida(_ordemServicoId, Inicio));

        var resultado = await CriarUseCases().ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.Equal(Inicio, ObterGravada().RemovidaEm);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ForaDeOrdem_StatusAntesDaValidacao_DeveCompletarOItemMantendoOStatus()
    {
        var useCases = CriarUseCases();

        await useCases.ProcessarStatusAlteradoAsync(CriarStatusAlterado(StatusOrdemServico.Recebida, StatusOrdemServico.EmDiagnostico));
        await useCases.ProcessarValidadaAsync(CriarValidada("Troca de oleo"));

        var execucao = ObterGravada();
        Assert.Equal(StatusOrdemServico.EmDiagnostico, execucao.StatusOrdemServico);
        Assert.Equal(_veiculoId, execucao.VeiculoId);
        Assert.Equal(["Troca de oleo"], execucao.Servicos);
        Assert.Equal(EtapaExecucao.EmDiagnostico, execucao.Etapa);
    }

    [Fact]
    public async Task ForaDeOrdem_CanceladaAntesDaValidacao_DeveContinuarForaDaProducao()
    {
        var useCases = CriarUseCases();

        await useCases.ProcessarStatusAlteradoAsync(CriarStatusAlterado(StatusOrdemServico.Recebida, StatusOrdemServico.Cancelada));
        var resultado = await useCases.ProcessarValidadaAsync(CriarValidada("Troca de oleo"));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        var execucao = ObterGravada();
        Assert.Equal(StatusOrdemServico.Cancelada, execucao.StatusOrdemServico);
        Assert.Equal(_veiculoId, execucao.VeiculoId);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Fact]
    public async Task ForaDeOrdem_ExcluidaAntesDaValidacao_DeveContinuarForaDaProducao()
    {
        var useCases = CriarUseCases();

        await useCases.ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId));
        var resultado = await useCases.ProcessarValidadaAsync(CriarValidada("Troca de oleo"));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        var execucao = ObterGravada();
        Assert.NotNull(execucao.RemovidaEm);
        Assert.Equal(_veiculoId, execucao.VeiculoId);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }
}
