using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;
using Xunit;

namespace TechChallenge.Oficina.Execution.Entities.Tests.Features.Execucoes;

public sealed class ExecucaoDomainTests
{
    private const string MensagemRemovida = "A ordem de servico foi excluida e saiu da producao.";
    private const string MensagemFinalizada = "A execucao da ordem de servico ja foi finalizada.";

    private static readonly DateTime Inicio = new(2026, 11, 19, 10, 00, 00, DateTimeKind.Utc);

    private static Execucao CriarValidada()
        => Execucao.CriarPorValidacao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ["Troca de oleo"], Inicio);

    // Item com o status espelhado do OS Service, como depois do OrdemServicoStatusAlterado
    private static Execucao CriarNoStatus(StatusOrdemServico status)
    {
        var execucao = CriarValidada();
        execucao.AtualizarStatus(status, Inicio.AddMinutes(1));
        return execucao;
    }

    private static Execucao CriarComExecucaoIniciada()
    {
        var execucao = CriarNoStatus(StatusOrdemServico.EmExecucao);
        execucao.IniciarExecucao(Inicio.AddMinutes(5));
        return execucao;
    }

    [Fact]
    public void CriarPorValidacao_DeveGuardarDadosComoRecebidaAguardandoDiagnostico()
    {
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();

        var execucao = Execucao.CriarPorValidacao(ordemServicoId, clienteId, veiculoId, [" Troca de oleo ", "Alinhamento"], Inicio);

        Assert.Equal(ordemServicoId, execucao.Id);
        Assert.Equal(ordemServicoId, execucao.OrdemServicoId);
        Assert.Equal(clienteId, execucao.ClienteId);
        Assert.Equal(veiculoId, execucao.VeiculoId);
        Assert.Equal(["Troca de oleo", "Alinhamento"], execucao.Servicos);
        Assert.Equal(StatusOrdemServico.Recebida, execucao.StatusOrdemServico);
        Assert.Equal(Inicio, execucao.CriadoEm);
        Assert.Null(execucao.DiagnosticoIniciadoEm);
        Assert.Null(execucao.EntrouNaFilaEm);
        Assert.Null(execucao.ExecucaoIniciadaEm);
        Assert.Null(execucao.FinalizadaEm);
        Assert.Null(execucao.RemovidaEm);
        Assert.Equal(EtapaExecucao.AguardandoDiagnostico, execucao.Etapa);
    }

    [Fact]
    public void CriarPorValidacao_DeveLancar_QuandoOrdemServicoIdVazio()
    {
        var action = () => Execucao.CriarPorValidacao(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), ["Troca de oleo"], Inicio);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O identificador da ordem de servico e obrigatorio.", exception.Message);
    }

    [Theory]
    [InlineData(true, false, 1, "O cliente da ordem de servico e obrigatorio.")]
    [InlineData(false, true, 1, "O veiculo da ordem de servico e obrigatorio.")]
    [InlineData(false, false, 0, "A ordem de servico deve possuir ao menos um servico.")]
    public void CriarPorValidacao_DeveLancar_QuandoDadosInvalidos(bool clienteVazio, bool veiculoVazio, int quantidadeServicos, string mensagem)
    {
        var servicos = Enumerable.Range(1, quantidadeServicos).Select(indice => $"Servico {indice}").ToArray();

        var action = () => Execucao.CriarPorValidacao(
            Guid.NewGuid(),
            clienteVazio ? Guid.Empty : Guid.NewGuid(),
            veiculoVazio ? Guid.Empty : Guid.NewGuid(),
            servicos,
            Inicio);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(mensagem, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CriarPorValidacao_DeveLancar_QuandoServicoSemNome(string nome)
    {
        var action = () => Execucao.CriarPorValidacao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ["Troca de oleo", nome], Inicio);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Todos os servicos informados devem possuir nome.", exception.Message);
    }

    [Fact]
    public void CriarPorValidacao_DeveLancar_QuandoServicosNulos()
    {
        var action = () => Execucao.CriarPorValidacao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null!, Inicio);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A ordem de servico deve possuir ao menos um servico.", exception.Message);
    }

    [Fact]
    public void CriarParcial_DeveCriarSemDadosComOStatusDoEvento()
    {
        var clienteId = Guid.NewGuid();

        var execucao = Execucao.CriarParcial(Guid.NewGuid(), clienteId, StatusOrdemServico.EmDiagnostico, Inicio);

        Assert.Equal(clienteId, execucao.ClienteId);
        Assert.Equal(Guid.Empty, execucao.VeiculoId);
        Assert.Empty(execucao.Servicos);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, execucao.StatusOrdemServico);
        Assert.Equal(Inicio, execucao.CriadoEm);
        Assert.Null(execucao.EntrouNaFilaEm);
        Assert.Equal(EtapaExecucao.EmDiagnostico, execucao.Etapa);
    }

    [Fact]
    public void CriarParcial_DeveRegistrarEntradaNaFila_QuandoStatusEmExecucao()
    {
        var execucao = Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmExecucao, Inicio);

        Assert.Equal(Inicio, execucao.EntrouNaFilaEm);
        Assert.Equal(EtapaExecucao.AguardandoExecucao, execucao.Etapa);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Encerrada)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void CriarParcial_DeveFicarForaDaProducao_QuandoStatusFinal(StatusOrdemServico status)
    {
        var execucao = Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), status, Inicio);

        Assert.Equal(status, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void CriarParcial_DeveLancar_QuandoStatusInvalido(int status)
    {
        var action = () => Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), (StatusOrdemServico)status, Inicio);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O status da ordem de servico informado e invalido.", exception.Message);
    }

    [Fact]
    public void CriarParcial_DeveLancar_QuandoOrdemServicoIdVazio()
    {
        var action = () => Execucao.CriarParcial(Guid.Empty, Guid.NewGuid(), StatusOrdemServico.Recebida, Inicio);

        Assert.Throws<DomainException>(action);
    }

    [Fact]
    public void CriarRemovida_DeveCriarTombstoneForaDaProducao()
    {
        var ordemServicoId = Guid.NewGuid();

        var execucao = Execucao.CriarRemovida(ordemServicoId, Inicio);

        Assert.Equal(ordemServicoId, execucao.Id);
        Assert.Equal(Inicio, execucao.RemovidaEm);
        Assert.Equal(Inicio, execucao.CriadoEm);
        Assert.Equal(Guid.Empty, execucao.ClienteId);
        Assert.Empty(execucao.Servicos);
        Assert.Equal(StatusOrdemServico.Recebida, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Fact]
    public void CriarRemovida_DeveLancar_QuandoOrdemServicoIdVazio()
    {
        var action = () => Execucao.CriarRemovida(Guid.Empty, Inicio);

        Assert.Throws<DomainException>(action);
    }

    [Fact]
    public void AtualizarDados_DeveTrocarDadosMantendoStatusEDatas()
    {
        var execucao = CriarComExecucaoIniciada();
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();

        execucao.AtualizarDados(clienteId, veiculoId, ["Balanceamento"]);

        Assert.Equal(clienteId, execucao.ClienteId);
        Assert.Equal(veiculoId, execucao.VeiculoId);
        Assert.Equal(["Balanceamento"], execucao.Servicos);
        Assert.Equal(StatusOrdemServico.EmExecucao, execucao.StatusOrdemServico);
        Assert.Equal(Inicio.AddMinutes(1), execucao.EntrouNaFilaEm);
        Assert.Equal(Inicio.AddMinutes(5), execucao.ExecucaoIniciadaEm);
        Assert.Equal(EtapaExecucao.EmExecucao, execucao.Etapa);
    }

    [Theory]
    [InlineData(true, false, 1, "O cliente da ordem de servico e obrigatorio.")]
    [InlineData(false, true, 1, "O veiculo da ordem de servico e obrigatorio.")]
    [InlineData(false, false, 0, "A ordem de servico deve possuir ao menos um servico.")]
    public void AtualizarDados_DeveLancarSemAlterar_QuandoDadosInvalidos(bool clienteVazio, bool veiculoVazio, int quantidadeServicos, string mensagem)
    {
        var execucao = CriarValidada();
        var clienteOriginal = execucao.ClienteId;
        var veiculoOriginal = execucao.VeiculoId;
        var servicos = Enumerable.Range(1, quantidadeServicos).Select(indice => $"Servico {indice}").ToArray();

        var action = () => execucao.AtualizarDados(clienteVazio ? Guid.Empty : Guid.NewGuid(), veiculoVazio ? Guid.Empty : Guid.NewGuid(), servicos);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(mensagem, exception.Message);
        Assert.Equal(clienteOriginal, execucao.ClienteId);
        Assert.Equal(veiculoOriginal, execucao.VeiculoId);
        Assert.Equal(["Troca de oleo"], execucao.Servicos);
    }

    [Fact]
    public void AtualizarDados_DeveCompletarItemParcialMantendoAEtapa()
    {
        var execucao = Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmDiagnostico, Inicio);
        var veiculoId = Guid.NewGuid();

        execucao.AtualizarDados(execucao.ClienteId, veiculoId, ["Troca de oleo"]);

        Assert.Equal(veiculoId, execucao.VeiculoId);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.EmDiagnostico, execucao.Etapa);
    }

    [Fact]
    public void AtualizarDados_DeveManterTombstoneForaDaProducao()
    {
        var execucao = Execucao.CriarRemovida(Guid.NewGuid(), Inicio);

        execucao.AtualizarDados(Guid.NewGuid(), Guid.NewGuid(), ["Troca de oleo"]);

        Assert.Equal(Inicio, execucao.RemovidaEm);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Fact]
    public void AtualizarStatus_DeveRegistrarEntradaNaFila_QuandoPassaParaEmExecucao()
    {
        var execucao = CriarNoStatus(StatusOrdemServico.AguardandoAprovacao);

        execucao.AtualizarStatus(StatusOrdemServico.EmExecucao, Inicio.AddMinutes(30));

        Assert.Equal(StatusOrdemServico.EmExecucao, execucao.StatusOrdemServico);
        Assert.Equal(Inicio.AddMinutes(30), execucao.EntrouNaFilaEm);
        Assert.Equal(EtapaExecucao.AguardandoExecucao, execucao.Etapa);
    }

    [Fact]
    public void AtualizarStatus_DeveManterAPrimeiraEntradaNaFila()
    {
        var execucao = Execucao.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmExecucao, Inicio);

        execucao.AtualizarStatus(StatusOrdemServico.Finalizada, Inicio.AddMinutes(10));
        execucao.AtualizarStatus(StatusOrdemServico.EmExecucao, Inicio.AddMinutes(20));

        Assert.Equal(Inicio, execucao.EntrouNaFilaEm);
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void AtualizarStatus_NaoDeveRegistrarEntradaNaFila_QuandoOutroStatus(StatusOrdemServico status)
    {
        var execucao = CriarValidada();

        execucao.AtualizarStatus(status, Inicio.AddMinutes(10));

        Assert.Equal(status, execucao.StatusOrdemServico);
        Assert.Null(execucao.EntrouNaFilaEm);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void AtualizarStatus_DeveLancarSemAlterar_QuandoStatusInvalido(int status)
    {
        var execucao = CriarValidada();

        var action = () => execucao.AtualizarStatus((StatusOrdemServico)status, Inicio.AddMinutes(1));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O status da ordem de servico informado e invalido.", exception.Message);
        Assert.Equal(StatusOrdemServico.Recebida, execucao.StatusOrdemServico);
    }

    [Fact]
    public void Remover_DeveTirarDaProducaoMantendoAPrimeiraData()
    {
        var execucao = CriarNoStatus(StatusOrdemServico.EmExecucao);

        execucao.Remover(Inicio.AddHours(1));
        execucao.Remover(Inicio.AddHours(2));

        Assert.Equal(Inicio.AddHours(1), execucao.RemovidaEm);
        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Recebida, false, false, EtapaExecucao.AguardandoDiagnostico)]
    [InlineData(StatusOrdemServico.Recebida, true, false, EtapaExecucao.EmDiagnostico)]
    [InlineData(StatusOrdemServico.EmDiagnostico, false, false, EtapaExecucao.EmDiagnostico)]
    [InlineData(StatusOrdemServico.EmDiagnostico, true, false, EtapaExecucao.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao, true, false, EtapaExecucao.AguardandoAprovacao)]
    [InlineData(StatusOrdemServico.EmExecucao, true, false, EtapaExecucao.AguardandoExecucao)]
    [InlineData(StatusOrdemServico.EmExecucao, true, true, EtapaExecucao.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada, true, false, EtapaExecucao.ForaDaProducao)]
    [InlineData(StatusOrdemServico.Entregue, true, false, EtapaExecucao.ForaDaProducao)]
    [InlineData(StatusOrdemServico.Encerrada, true, false, EtapaExecucao.ForaDaProducao)]
    [InlineData(StatusOrdemServico.Cancelada, false, false, EtapaExecucao.ForaDaProducao)]
    public void Etapa_DeveSerCalculadaPeloStatusEPelasDatas(StatusOrdemServico status, bool diagnosticoIniciado, bool execucaoIniciada, EtapaExecucao esperada)
    {
        var execucao = CriarValidada();

        if (diagnosticoIniciado)
        {
            execucao.IniciarDiagnostico(Inicio.AddMinutes(1));
        }

        execucao.AtualizarStatus(status, Inicio.AddMinutes(2));

        if (execucaoIniciada)
        {
            execucao.IniciarExecucao(Inicio.AddMinutes(3));
        }

        Assert.Equal(esperada, execucao.Etapa);
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    public void Etapa_DeveSerFinalizada_QuandoAExecucaoFoiFinalizadaAqui(StatusOrdemServico statusDepois)
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Finalizar(Inicio.AddMinutes(30));

        execucao.AtualizarStatus(statusDepois, Inicio.AddMinutes(31));

        Assert.Equal(EtapaExecucao.Finalizada, execucao.Etapa);
    }

    [Fact]
    public void Etapa_DeveSerForaDaProducao_QuandoRemovidaMesmoJaFinalizada()
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Finalizar(Inicio.AddMinutes(30));

        execucao.Remover(Inicio.AddMinutes(40));

        Assert.Equal(EtapaExecucao.ForaDaProducao, execucao.Etapa);
    }

    [Fact]
    public void IniciarDiagnostico_DeveRegistrarDataSemMudarStatus_NaPrimeiraVez()
    {
        var execucao = CriarValidada();

        var repeticao = execucao.IniciarDiagnostico(Inicio.AddMinutes(10));

        Assert.False(repeticao);
        Assert.Equal(Inicio.AddMinutes(10), execucao.DiagnosticoIniciadoEm);
        Assert.Equal(StatusOrdemServico.Recebida, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.EmDiagnostico, execucao.Etapa);
    }

    [Fact]
    public void IniciarDiagnostico_DeveManterADataEIndicarRepeticao_QuandoOsServiceAindaNaoConfirmou()
    {
        var execucao = CriarValidada();
        execucao.IniciarDiagnostico(Inicio.AddMinutes(10));

        var repeticao = execucao.IniciarDiagnostico(Inicio.AddMinutes(20));

        Assert.True(repeticao);
        Assert.Equal(Inicio.AddMinutes(10), execucao.DiagnosticoIniciadoEm);
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(StatusOrdemServico.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Encerrada)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void IniciarDiagnostico_DeveLancar_QuandoOrdemNaoEstaRecebida(StatusOrdemServico status)
    {
        var execucao = CriarNoStatus(status);

        Action action = () => execucao.IniciarDiagnostico(Inicio.AddMinutes(10));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente ordem de servico recebida pode ter o diagnostico iniciado.", exception.Message);
        Assert.Null(execucao.DiagnosticoIniciadoEm);
    }

    [Fact]
    public void IniciarDiagnostico_DeveLancar_QuandoRemovida()
    {
        var execucao = CriarValidada();
        execucao.Remover(Inicio.AddMinutes(5));

        Action action = () => execucao.IniciarDiagnostico(Inicio.AddMinutes(10));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(MensagemRemovida, exception.Message);
    }

    [Fact]
    public void IniciarDiagnostico_DeveLancar_QuandoExecucaoJaFinalizada()
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Finalizar(Inicio.AddMinutes(30));

        Action action = () => execucao.IniciarDiagnostico(Inicio.AddMinutes(40));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(MensagemFinalizada, exception.Message);
    }

    [Fact]
    public void IniciarExecucao_DeveRegistrarDataSemMudarStatus_QuandoPagamentoAprovado()
    {
        var execucao = CriarNoStatus(StatusOrdemServico.EmExecucao);

        execucao.IniciarExecucao(Inicio.AddMinutes(15));

        Assert.Equal(Inicio.AddMinutes(15), execucao.ExecucaoIniciadaEm);
        Assert.Equal(StatusOrdemServico.EmExecucao, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.EmExecucao, execucao.Etapa);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Recebida)]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Encerrada)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void IniciarExecucao_DeveLancar_QuandoOrdemNaoEstaEmExecucao(StatusOrdemServico status)
    {
        var execucao = CriarNoStatus(status);

        var action = () => execucao.IniciarExecucao(Inicio.AddMinutes(15));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente ordem de servico em execucao (pagamento aprovado) pode ter a execucao iniciada.", exception.Message);
        Assert.Null(execucao.ExecucaoIniciadaEm);
    }

    [Fact]
    public void IniciarExecucao_DeveLancarMantendoAData_QuandoJaIniciada()
    {
        var execucao = CriarComExecucaoIniciada();

        var action = () => execucao.IniciarExecucao(Inicio.AddMinutes(20));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A execucao da ordem de servico ja foi iniciada.", exception.Message);
        Assert.Equal(Inicio.AddMinutes(5), execucao.ExecucaoIniciadaEm);
    }

    [Fact]
    public void IniciarExecucao_DeveLancar_QuandoJaFinalizada()
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Finalizar(Inicio.AddMinutes(30));

        var action = () => execucao.IniciarExecucao(Inicio.AddMinutes(40));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(MensagemFinalizada, exception.Message);
    }

    [Fact]
    public void IniciarExecucao_DeveLancar_QuandoRemovida()
    {
        var execucao = CriarNoStatus(StatusOrdemServico.EmExecucao);
        execucao.Remover(Inicio.AddMinutes(5));

        var action = () => execucao.IniciarExecucao(Inicio.AddMinutes(10));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(MensagemRemovida, exception.Message);
    }

    [Fact]
    public void Finalizar_DeveRegistrarDataSemMudarStatus_NaPrimeiraVez()
    {
        var execucao = CriarComExecucaoIniciada();

        var repeticao = execucao.Finalizar(Inicio.AddMinutes(30));

        Assert.False(repeticao);
        Assert.Equal(Inicio.AddMinutes(30), execucao.FinalizadaEm);
        Assert.Equal(StatusOrdemServico.EmExecucao, execucao.StatusOrdemServico);
        Assert.Equal(EtapaExecucao.Finalizada, execucao.Etapa);
    }

    [Fact]
    public void Finalizar_DeveManterADataEIndicarRepeticao_QuandoOsServiceAindaNaoConfirmou()
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Finalizar(Inicio.AddMinutes(30));

        var repeticao = execucao.Finalizar(Inicio.AddMinutes(45));

        Assert.True(repeticao);
        Assert.Equal(Inicio.AddMinutes(30), execucao.FinalizadaEm);
    }

    [Fact]
    public void Finalizar_DeveLancar_QuandoExecucaoNaoFoiIniciada()
    {
        var execucao = CriarNoStatus(StatusOrdemServico.EmExecucao);

        Action action = () => execucao.Finalizar(Inicio.AddMinutes(30));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A execucao da ordem de servico precisa ser iniciada antes de ser finalizada.", exception.Message);
        Assert.Null(execucao.FinalizadaEm);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Recebida)]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void Finalizar_DeveLancar_QuandoOrdemNaoEstaEmExecucao(StatusOrdemServico status)
    {
        var execucao = CriarNoStatus(status);

        Action action = () => execucao.Finalizar(Inicio.AddMinutes(30));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente ordem de servico em execucao pode ser finalizada.", exception.Message);
    }

    [Fact]
    public void Finalizar_DeveLancar_DepoisDaConfirmacaoDoOsService()
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Finalizar(Inicio.AddMinutes(30));
        execucao.AtualizarStatus(StatusOrdemServico.Finalizada, Inicio.AddMinutes(31));

        Action action = () => execucao.Finalizar(Inicio.AddMinutes(40));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente ordem de servico em execucao pode ser finalizada.", exception.Message);
        Assert.Equal(Inicio.AddMinutes(30), execucao.FinalizadaEm);
    }

    [Fact]
    public void Finalizar_DeveLancar_QuandoRemovida()
    {
        var execucao = CriarComExecucaoIniciada();
        execucao.Remover(Inicio.AddMinutes(10));

        Action action = () => execucao.Finalizar(Inicio.AddMinutes(30));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(MensagemRemovida, exception.Message);
        Assert.Null(execucao.FinalizadaEm);
    }
}
