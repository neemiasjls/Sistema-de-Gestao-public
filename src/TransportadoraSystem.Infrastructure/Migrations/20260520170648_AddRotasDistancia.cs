using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TransportadoraSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRotasDistancia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Abastecimentos_Postos_PostoId",
                table: "Abastecimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_Abastecimentos_Veiculos_VeiculoId",
                table: "Abastecimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_AcertosCaminhao_Motoristas_MotoristaId",
                table: "AcertosCaminhao");

            migrationBuilder.DropForeignKey(
                name: "FK_AcertosCaminhao_Veiculos_VeiculoId",
                table: "AcertosCaminhao");

            migrationBuilder.DropForeignKey(
                name: "FK_Boletos_Clientes_ClienteId",
                table: "Boletos");

            migrationBuilder.DropForeignKey(
                name: "FK_Boletos_Romaneios_RomaneioId",
                table: "Boletos");

            migrationBuilder.DropForeignKey(
                name: "FK_Boletos_Veiculos_VeiculoId",
                table: "Boletos");

            migrationBuilder.DropForeignKey(
                name: "FK_CartaoFreteTransacoes_Veiculos_VeiculoId",
                table: "CartaoFreteTransacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_DespesasBarracao_LotesVolumesEmbalados_LoteId",
                table: "DespesasBarracao");

            migrationBuilder.DropForeignKey(
                name: "FK_LancamentosBanco_Veiculos_VeiculoId",
                table: "LancamentosBanco");

            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosEstoque_Clientes_ClienteId",
                table: "MovimentosEstoque");

            migrationBuilder.DropForeignKey(
                name: "FK_Romaneios_Clientes_ClienteId",
                table: "Romaneios");

            migrationBuilder.DropForeignKey(
                name: "FK_SaldosReceber_Veiculos_VeiculoId",
                table: "SaldosReceber");

            migrationBuilder.DropForeignKey(
                name: "FK_SegurosPlaca_Veiculos_VeiculoId",
                table: "SegurosPlaca");

            migrationBuilder.DropForeignKey(
                name: "FK_SemPararPlacas_Veiculos_VeiculoId",
                table: "SemPararPlacas");

            migrationBuilder.DropForeignKey(
                name: "FK_Viagens_Motoristas_MotoristaId",
                table: "Viagens");

            migrationBuilder.DropForeignKey(
                name: "FK_Viagens_Veiculos_VeiculoId",
                table: "Viagens");

            migrationBuilder.DropIndex(
                name: "IX_CTes_ChaveAcesso",
                table: "CTes");

            migrationBuilder.CreateTable(
                name: "RotasDistancia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Origem = table.Column<string>(type: "text", nullable: false),
                    UFOrigem = table.Column<string>(type: "text", nullable: false),
                    Destino = table.Column<string>(type: "text", nullable: false),
                    UF = table.Column<string>(type: "text", nullable: false),
                    Km = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotasDistancia", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "RotasDistancia",
                columns: new[] { "Id", "AtualizadoEm", "CriadoEm", "Destino", "Km", "Origem", "UF", "UFOrigem" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "ANAPOLIS", 774, "ALDEIA", "GO", "SP" },
                    { 2, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "ITUMBIARA", 517, "ALDEIA", "GO", "SP" },
                    { 3, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "GOIANIA", 722, "ALDEIA", "GO", "SP" },
                    { 4, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "APARECIDA DE GOIANIA", 704, "ALDEIA", "GO", "SP" },
                    { 5, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "HIDROLANDIA", 685, "ALDEIA", "GO", "SP" },
                    { 6, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "CALDAS NOVAS", 642, "ALDEIA", "GO", "SP" },
                    { 7, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "FAROL", 1212, "ALDEIA", "GO", "SP" },
                    { 8, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "SILVANIA", 779, "ALDEIA", "GO", "SP" },
                    { 9, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "PARAISO DO TOCANTINS", 1505, "ALDEIA", "TO", "SP" },
                    { 10, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "PALMAS", 1542, "ALDEIA", "TO", "SP" },
                    { 11, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "BOM JESUS DE GOIAS", 582, "ALDEIA", "GO", "SP" },
                    { 12, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "CHAPADA DA NATIVIDADE", 1452, "ALDEIA", "TO", "SP" },
                    { 13, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "ANAPOLIS", 812, "RIBEIRA", "GO", "SP" },
                    { 14, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "ITUMBIARA", 555, "RIBEIRA", "GO", "SP" },
                    { 15, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "GOIANIA", 760, "RIBEIRA", "GO", "SP" },
                    { 16, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "APARECIDA DE GOIANIA", 741, "RIBEIRA", "GO", "SP" },
                    { 17, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "HIDROLANDIA", 724, "RIBEIRA", "GO", "SP" },
                    { 18, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "CALDAS NOVAS", 680, "RIBEIRA", "GO", "SP" },
                    { 19, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "FAROL", 1250, "RIBEIRA", "GO", "SP" },
                    { 20, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "SILVANIA", 817, "RIBEIRA", "GO", "SP" },
                    { 21, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "BOM JESUS DE GOIAS", 620, "RIBEIRA", "GO", "SP" },
                    { 22, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "PARAISO DO TOCANTINS", 1543, "RIBEIRA", "TO", "SP" },
                    { 23, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "PALMAS", 1580, "RIBEIRA", "TO", "SP" },
                    { 24, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "CHAPADA DA NATIVIDADE", 1491, "RIBEIRA", "TO", "SP" },
                    { 25, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "GOIANIA", 712, "CASCATA", "GO", "SP" },
                    { 26, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "PALMAS", 1532, "CASCATA", "TO", "SP" },
                    { 27, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "GOIANIA", 702, "SERRANA", "GO", "SP" },
                    { 28, new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 5, 5, 0, 0, 0, 0, DateTimeKind.Utc), "BRASILIA", 977, "VILA SUL", "DF", "SP" }
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Abastecimentos_Postos_PostoId",
                table: "Abastecimentos",
                column: "PostoId",
                principalTable: "Postos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Abastecimentos_Veiculos_VeiculoId",
                table: "Abastecimentos",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AcertosCaminhao_Motoristas_MotoristaId",
                table: "AcertosCaminhao",
                column: "MotoristaId",
                principalTable: "Motoristas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AcertosCaminhao_Veiculos_VeiculoId",
                table: "AcertosCaminhao",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Boletos_Clientes_ClienteId",
                table: "Boletos",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Boletos_Romaneios_RomaneioId",
                table: "Boletos",
                column: "RomaneioId",
                principalTable: "Romaneios",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Boletos_Veiculos_VeiculoId",
                table: "Boletos",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CartaoFreteTransacoes_Veiculos_VeiculoId",
                table: "CartaoFreteTransacoes",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DespesasBarracao_LotesVolumesEmbalados_LoteId",
                table: "DespesasBarracao",
                column: "LoteId",
                principalTable: "LotesVolumesEmbalados",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LancamentosBanco_Veiculos_VeiculoId",
                table: "LancamentosBanco",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosEstoque_Clientes_ClienteId",
                table: "MovimentosEstoque",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Romaneios_Clientes_ClienteId",
                table: "Romaneios",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SaldosReceber_Veiculos_VeiculoId",
                table: "SaldosReceber",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SegurosPlaca_Veiculos_VeiculoId",
                table: "SegurosPlaca",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SemPararPlacas_Veiculos_VeiculoId",
                table: "SemPararPlacas",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Viagens_Motoristas_MotoristaId",
                table: "Viagens",
                column: "MotoristaId",
                principalTable: "Motoristas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Viagens_Veiculos_VeiculoId",
                table: "Viagens",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Abastecimentos_Postos_PostoId",
                table: "Abastecimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_Abastecimentos_Veiculos_VeiculoId",
                table: "Abastecimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_AcertosCaminhao_Motoristas_MotoristaId",
                table: "AcertosCaminhao");

            migrationBuilder.DropForeignKey(
                name: "FK_AcertosCaminhao_Veiculos_VeiculoId",
                table: "AcertosCaminhao");

            migrationBuilder.DropForeignKey(
                name: "FK_Boletos_Clientes_ClienteId",
                table: "Boletos");

            migrationBuilder.DropForeignKey(
                name: "FK_Boletos_Romaneios_RomaneioId",
                table: "Boletos");

            migrationBuilder.DropForeignKey(
                name: "FK_Boletos_Veiculos_VeiculoId",
                table: "Boletos");

            migrationBuilder.DropForeignKey(
                name: "FK_CartaoFreteTransacoes_Veiculos_VeiculoId",
                table: "CartaoFreteTransacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_DespesasBarracao_LotesVolumesEmbalados_LoteId",
                table: "DespesasBarracao");

            migrationBuilder.DropForeignKey(
                name: "FK_LancamentosBanco_Veiculos_VeiculoId",
                table: "LancamentosBanco");

            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosEstoque_Clientes_ClienteId",
                table: "MovimentosEstoque");

            migrationBuilder.DropForeignKey(
                name: "FK_Romaneios_Clientes_ClienteId",
                table: "Romaneios");

            migrationBuilder.DropForeignKey(
                name: "FK_SaldosReceber_Veiculos_VeiculoId",
                table: "SaldosReceber");

            migrationBuilder.DropForeignKey(
                name: "FK_SegurosPlaca_Veiculos_VeiculoId",
                table: "SegurosPlaca");

            migrationBuilder.DropForeignKey(
                name: "FK_SemPararPlacas_Veiculos_VeiculoId",
                table: "SemPararPlacas");

            migrationBuilder.DropForeignKey(
                name: "FK_Viagens_Motoristas_MotoristaId",
                table: "Viagens");

            migrationBuilder.DropForeignKey(
                name: "FK_Viagens_Veiculos_VeiculoId",
                table: "Viagens");

            migrationBuilder.DropTable(
                name: "RotasDistancia");

            migrationBuilder.CreateIndex(
                name: "IX_CTes_ChaveAcesso",
                table: "CTes",
                column: "ChaveAcesso");

            migrationBuilder.AddForeignKey(
                name: "FK_Abastecimentos_Postos_PostoId",
                table: "Abastecimentos",
                column: "PostoId",
                principalTable: "Postos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Abastecimentos_Veiculos_VeiculoId",
                table: "Abastecimentos",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcertosCaminhao_Motoristas_MotoristaId",
                table: "AcertosCaminhao",
                column: "MotoristaId",
                principalTable: "Motoristas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcertosCaminhao_Veiculos_VeiculoId",
                table: "AcertosCaminhao",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Boletos_Clientes_ClienteId",
                table: "Boletos",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Boletos_Romaneios_RomaneioId",
                table: "Boletos",
                column: "RomaneioId",
                principalTable: "Romaneios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Boletos_Veiculos_VeiculoId",
                table: "Boletos",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CartaoFreteTransacoes_Veiculos_VeiculoId",
                table: "CartaoFreteTransacoes",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DespesasBarracao_LotesVolumesEmbalados_LoteId",
                table: "DespesasBarracao",
                column: "LoteId",
                principalTable: "LotesVolumesEmbalados",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_LancamentosBanco_Veiculos_VeiculoId",
                table: "LancamentosBanco",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosEstoque_Clientes_ClienteId",
                table: "MovimentosEstoque",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Romaneios_Clientes_ClienteId",
                table: "Romaneios",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaldosReceber_Veiculos_VeiculoId",
                table: "SaldosReceber",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SegurosPlaca_Veiculos_VeiculoId",
                table: "SegurosPlaca",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SemPararPlacas_Veiculos_VeiculoId",
                table: "SemPararPlacas",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Viagens_Motoristas_MotoristaId",
                table: "Viagens",
                column: "MotoristaId",
                principalTable: "Motoristas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Viagens_Veiculos_VeiculoId",
                table: "Viagens",
                column: "VeiculoId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
