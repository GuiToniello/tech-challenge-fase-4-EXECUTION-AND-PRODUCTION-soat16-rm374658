using TechChallenge.Oficina.Execution.Entities.Exceptions;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes.Enums;
using TechChallenge.Oficina.Execution.Entities.Features.OrdensServico.Enums;

namespace TechChallenge.Oficina.Execution.Entities.Features.Execucoes;

// Raiz de agregado da producao: o Id e o mesmo da ordem de servico (OS Service), ja que cada OS passa uma unica vez pela oficina.
// Os dados (cliente, veiculo e nomes dos servicos) vem do OrdemServicoValidada (Management) e o status e o espelho do OrdemServicoStatusAlterado (OS Service).
// As acoes do mecanico (diagnostico, inicio e fim da execucao) so registram datas: quem muda o status e o OS Service, ao consumir os eventos publicados aqui.
public sealed class Execucao
{
    private const string MensagemRemovida = "A ordem de servico foi excluida e saiu da producao.";
    private const string MensagemFinalizada = "A execucao da ordem de servico ja foi finalizada.";

    // Nao e readonly: o EF Core materializa a coluna servicos atribuindo uma nova lista a este campo.
    private List<string> _servicos = [];

    public Guid Id { get; private set; }
    public Guid OrdemServicoId => Id;
    public Guid ClienteId { get; private set; }

    // Vazio enquanto o OrdemServicoValidada nao chega (item criado pelo status ou pela exclusao da OS)
    public Guid VeiculoId { get; private set; }

    // So os nomes dos servicos, como chegaram da Management: e o que o mecanico precisa ver na fila
    public IReadOnlyCollection<string> Servicos => _servicos.AsReadOnly();

    // Espelho do status do OS Service: so os eventos o alteram, nunca as acoes HTTP
    public StatusOrdemServico StatusOrdemServico { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime? DiagnosticoIniciadoEm { get; private set; }

    // Entrada na fila de execucao (status EmExecucao, depois do pagamento aprovado). Ordena a fila; nas etapas de diagnostico vale o CriadoEm.
    public DateTime? EntrouNaFilaEm { get; private set; }

    public DateTime? ExecucaoIniciadaEm { get; private set; }
    public DateTime? FinalizadaEm { get; private set; }

    // Exclusao da OS no OS Service: o registro fica (tombstone) para que um evento atrasado nao recoloque a OS na fila
    public DateTime? RemovidaEm { get; private set; }

    // Versao da linha (xmin do PostgreSQL), controlada pelo banco: concorrencia otimista entre o consumidor da fila e as acoes HTTP
    public uint Versao { get; private set; }

    public EtapaExecucao Etapa => ObterEtapa();

    private Execucao()
    {
    }

    private Execucao(Guid ordemServicoId, Guid clienteId, StatusOrdemServico status, DateTime agora)
    {
        if (ordemServicoId == Guid.Empty)
        {
            throw new DomainException("O identificador da ordem de servico e obrigatorio.");
        }

        Id = ordemServicoId;
        ClienteId = clienteId;
        CriadoEm = agora;
        AtualizarStatus(status, agora);
    }

    // Item completo (OrdemServicoValidada): sem status conhecido ainda, a OS e considerada recebida
    public static Execucao CriarPorValidacao(Guid ordemServicoId, Guid clienteId, Guid veiculoId, IReadOnlyCollection<string> servicos, DateTime agora)
    {
        var execucao = new Execucao(ordemServicoId, clienteId, StatusOrdemServico.Recebida, agora);
        execucao.AtualizarDados(clienteId, veiculoId, servicos);
        return execucao;
    }

    // Item parcial: o OrdemServicoStatusAlterado chegou antes do OrdemServicoValidada (publicadores diferentes, sem ordem garantida entre eles).
    // Tambem para os status finais (5 a 8): o registro impede que uma validacao atrasada coloque a OS na fila.
    public static Execucao CriarParcial(Guid ordemServicoId, Guid clienteId, StatusOrdemServico status, DateTime agora)
    {
        return new Execucao(ordemServicoId, clienteId, status, agora);
    }

    // Tombstone: o OrdemServicoExcluida chegou antes de qualquer outro evento da OS. O status nao e conhecido e fica Recebida; a etapa e ForaDaProducao.
    public static Execucao CriarRemovida(Guid ordemServicoId, DateTime agora)
    {
        var execucao = new Execucao(ordemServicoId, Guid.Empty, StatusOrdemServico.Recebida, agora);
        execucao.Remover(agora);
        return execucao;
    }

    // Mantem o status e as datas: o status pertence ao OS Service e pode ter chegado antes dos dados
    public void AtualizarDados(Guid clienteId, Guid veiculoId, IReadOnlyCollection<string> servicos)
    {
        if (clienteId == Guid.Empty)
        {
            throw new DomainException("O cliente da ordem de servico e obrigatorio.");
        }

        if (veiculoId == Guid.Empty)
        {
            throw new DomainException("O veiculo da ordem de servico e obrigatorio.");
        }

        if (servicos is null || servicos.Count == 0)
        {
            throw new DomainException("A ordem de servico deve possuir ao menos um servico.");
        }

        if (servicos.Any(string.IsNullOrWhiteSpace))
        {
            throw new DomainException("Todos os servicos informados devem possuir nome.");
        }

        ClienteId = clienteId;
        VeiculoId = veiculoId;
        _servicos = servicos.Select(servico => servico.Trim()).ToList();
    }

    public void AtualizarStatus(StatusOrdemServico status, DateTime agora)
    {
        if (!Enum.IsDefined(typeof(StatusOrdemServico), status))
        {
            throw new DomainException("O status da ordem de servico informado e invalido.");
        }

        // A OS entra na fila de execucao quando o pagamento e aprovado (EmExecucao); a data da primeira entrada e mantida
        if (status == StatusOrdemServico.EmExecucao && EntrouNaFilaEm is null)
        {
            EntrouNaFilaEm = agora;
        }

        StatusOrdemServico = status;
    }

    // Exclusao repetida mantem a data da primeira
    public void Remover(DateTime agora)
    {
        RemovidaEm ??= agora;
    }

    // Retorna se e uma repeticao: o diagnostico ja foi iniciado, mas o OS Service ainda nao confirmou (o status continua Recebida).
    // A data do primeiro pedido e mantida e o caso de uso publica o evento de novo, recuperando uma publicacao que tenha falhado.
    public bool IniciarDiagnostico(DateTime agora)
    {
        ValidarNaoRemovida();
        ValidarNaoFinalizada();

        if (StatusOrdemServico != StatusOrdemServico.Recebida)
        {
            throw new DomainException("Somente ordem de servico recebida pode ter o diagnostico iniciado.");
        }

        if (DiagnosticoIniciadoEm is not null)
        {
            return true;
        }

        DiagnosticoIniciadoEm = agora;
        return false;
    }

    // Controle interno da oficina (sem evento): o status EmExecucao ja veio do OS Service com o pagamento aprovado
    public void IniciarExecucao(DateTime agora)
    {
        ValidarNaoRemovida();
        ValidarNaoFinalizada();

        if (StatusOrdemServico != StatusOrdemServico.EmExecucao)
        {
            throw new DomainException("Somente ordem de servico em execucao (pagamento aprovado) pode ter a execucao iniciada.");
        }

        if (ExecucaoIniciadaEm is not null)
        {
            throw new DomainException("A execucao da ordem de servico ja foi iniciada.");
        }

        ExecucaoIniciadaEm = agora;
    }

    // Retorna se e uma repeticao: a execucao ja foi finalizada, mas o OS Service ainda nao confirmou (o status continua EmExecucao).
    // A data da primeira finalizacao e mantida e o caso de uso publica o evento de novo.
    public bool Finalizar(DateTime agora)
    {
        ValidarNaoRemovida();

        if (StatusOrdemServico != StatusOrdemServico.EmExecucao)
        {
            throw new DomainException("Somente ordem de servico em execucao pode ser finalizada.");
        }

        if (ExecucaoIniciadaEm is null)
        {
            throw new DomainException("A execucao da ordem de servico precisa ser iniciada antes de ser finalizada.");
        }

        if (FinalizadaEm is not null)
        {
            return true;
        }

        FinalizadaEm = agora;
        return false;
    }

    private EtapaExecucao ObterEtapa()
    {
        if (RemovidaEm is not null)
        {
            return EtapaExecucao.ForaDaProducao;
        }

        if (FinalizadaEm is not null)
        {
            return EtapaExecucao.Finalizada;
        }

        return StatusOrdemServico switch
        {
            StatusOrdemServico.Recebida => DiagnosticoIniciadoEm is null ? EtapaExecucao.AguardandoDiagnostico : EtapaExecucao.EmDiagnostico,
            StatusOrdemServico.EmDiagnostico => EtapaExecucao.EmDiagnostico,
            StatusOrdemServico.AguardandoAprovacao => EtapaExecucao.AguardandoAprovacao,
            StatusOrdemServico.EmExecucao => ExecucaoIniciadaEm is null ? EtapaExecucao.AguardandoExecucao : EtapaExecucao.EmExecucao,
            _ => EtapaExecucao.ForaDaProducao
        };
    }

    private void ValidarNaoRemovida()
    {
        if (RemovidaEm is not null)
        {
            throw new DomainException(MensagemRemovida);
        }
    }

    private void ValidarNaoFinalizada()
    {
        if (FinalizadaEm is not null)
        {
            throw new DomainException(MensagemFinalizada);
        }
    }
}
