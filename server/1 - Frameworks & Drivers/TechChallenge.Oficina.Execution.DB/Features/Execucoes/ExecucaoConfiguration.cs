using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechChallenge.Oficina.Execution.Entities.Features.Execucoes;

namespace TechChallenge.Oficina.Execution.DB.Data.Features.Execucoes;

// Item da producao (o dono da OS e o OS Service, os dados vem da Management): sem chaves estrangeiras para clientes, veiculos e servicos de outros bancos.
public sealed class ExecucaoConfiguration : IEntityTypeConfiguration<Execucao>
{
    private const string TipoDataHora = "timestamp with time zone";

    public void Configure(EntityTypeBuilder<Execucao> builder)
    {
        builder.ToTable("execucoes");

        builder.HasKey(execucao => execucao.Id);

        builder.Property(execucao => execucao.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Ignore(execucao => execucao.OrdemServicoId);

        // Calculada a partir do status e das datas: nao tem coluna
        builder.Ignore(execucao => execucao.Etapa);

        builder.Property(execucao => execucao.ClienteId)
            .HasColumnName("cliente_id")
            .IsRequired();

        builder.Property(execucao => execucao.VeiculoId)
            .HasColumnName("veiculo_id")
            .IsRequired();

        // Nomes dos servicos numa coluna text[] (primitive collection do Npgsql) lida e gravada pelo campo _servicos
        builder.PrimitiveCollection(execucao => execucao.Servicos)
            .HasField("_servicos")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("servicos")
            .IsRequired();

        builder.Property(execucao => execucao.StatusOrdemServico)
            .HasColumnName("status")
            .HasColumnType("integer")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(execucao => execucao.CriadoEm)
            .HasColumnName("criado_em")
            .HasColumnType(TipoDataHora)
            .IsRequired();

        builder.Property(execucao => execucao.DiagnosticoIniciadoEm)
            .HasColumnName("diagnostico_iniciado_em")
            .HasColumnType(TipoDataHora);

        builder.Property(execucao => execucao.EntrouNaFilaEm)
            .HasColumnName("entrou_na_fila_em")
            .HasColumnType(TipoDataHora);

        builder.Property(execucao => execucao.ExecucaoIniciadaEm)
            .HasColumnName("execucao_iniciada_em")
            .HasColumnType(TipoDataHora);

        builder.Property(execucao => execucao.FinalizadaEm)
            .HasColumnName("finalizada_em")
            .HasColumnType(TipoDataHora);

        builder.Property(execucao => execucao.RemovidaEm)
            .HasColumnName("removida_em")
            .HasColumnType(TipoDataHora);

        // Concorrencia otimista pela coluna de sistema xmin do PostgreSQL (nao e criada pela migration): o consumidor da fila e uma acao HTTP
        // alterando a mesma OS ao mesmo tempo nao sobrescrevem um ao outro (o segundo recebe DbUpdateConcurrencyException)
        builder.Property(execucao => execucao.Versao)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        builder.HasIndex(execucao => execucao.StatusOrdemServico);
    }
}
