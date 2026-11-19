using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechChallenge.Oficina.Execution.DB.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "execucoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    veiculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicos = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    diagnostico_iniciado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    entrou_na_fila_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    execucao_iniciada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finalizada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    removida_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execucoes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_execucoes_status",
                table: "execucoes",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "execucoes");
        }
    }
}
