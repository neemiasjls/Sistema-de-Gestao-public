using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TransportadoraSystem.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Motoristas (sem FK para Veiculos ainda — referência circular) ──
            migrationBuilder.CreateTable(
                name: "Motoristas",
                columns: table => new
                {
                    Id             = table.Column<int>(type: "integer", nullable: false)
                                          .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome           = table.Column<string>(type: "text", nullable: false),
                    Apelido        = table.Column<string>(type: "text", nullable: true),
                    CPF            = table.Column<string>(type: "text", nullable: true),
                    Telefone       = table.Column<string>(type: "text", nullable: true),
                    Ativo          = table.Column<bool>(type: "boolean", nullable: false),
                    VeiculoPrincipalId = table.Column<int>(type: "integer", nullable: true),
                    SocioPrincipal = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Motoristas", x => x.Id);
                });

            // ── Clientes ──
            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome        = table.Column<string>(type: "text", nullable: false),
                    NomeFantasia= table.Column<string>(type: "text", nullable: true),
                    CNPJ        = table.Column<string>(type: "text", nullable: true),
                    Email       = table.Column<string>(type: "text", nullable: true),
                    Telefone    = table.Column<string>(type: "text", nullable: true),
                    Cidade      = table.Column<string>(type: "text", nullable: true),
                    UF          = table.Column<string>(type: "text", nullable: true),
                    Ativo       = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                });

            // ── Fazendas ──
            migrationBuilder.CreateTable(
                name: "Fazendas",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome        = table.Column<string>(type: "text", nullable: false),
                    Contato     = table.Column<string>(type: "text", nullable: true),
                    Telefone    = table.Column<string>(type: "text", nullable: true),
                    Cidade      = table.Column<string>(type: "text", nullable: true),
                    UF          = table.Column<string>(type: "text", nullable: true),
                    Ativo       = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fazendas", x => x.Id);
                });

            // ── Postos ──
            migrationBuilder.CreateTable(
                name: "Postos",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome        = table.Column<string>(type: "text", nullable: false),
                    Cidade      = table.Column<string>(type: "text", nullable: true),
                    UF          = table.Column<string>(type: "text", nullable: true),
                    Ativo       = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postos", x => x.Id);
                });

            // ── DespesasDivididas ──
            migrationBuilder.CreateTable(
                name: "DespesasDivididas",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Descricao   = table.Column<string>(type: "text", nullable: false),
                    ValorTotal  = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Mes         = table.Column<int>(type: "integer", nullable: false),
                    Ano         = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DespesasDivididas", x => x.Id);
                });

            // ── SegurosMensais ──
            migrationBuilder.CreateTable(
                name: "SegurosMensais",
                columns: table => new
                {
                    Id            = table.Column<int>(type: "integer", nullable: false)
                                         .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Mes           = table.Column<int>(type: "integer", nullable: false),
                    Ano           = table.Column<int>(type: "integer", nullable: false),
                    ValorTotal    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Seguradora    = table.Column<string>(type: "text", nullable: true),
                    NumeroApolice = table.Column<string>(type: "text", nullable: true),
                    CriadoEm      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegurosMensais", x => x.Id);
                });

            // ── SemPararMensais ──
            migrationBuilder.CreateTable(
                name: "SemPararMensais",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Mes         = table.Column<int>(type: "integer", nullable: false),
                    Ano         = table.Column<int>(type: "integer", nullable: false),
                    ValorTotal  = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemPararMensais", x => x.Id);
                });

            // ── LotesVolumesEmbalados ──
            migrationBuilder.CreateTable(
                name: "LotesVolumesEmbalados",
                columns: table => new
                {
                    Id                   = table.Column<int>(type: "integer", nullable: false)
                                                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DataEntrada          = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Mes                  = table.Column<int>(type: "integer", nullable: false),
                    Ano                  = table.Column<int>(type: "integer", nullable: false),
                    QtdEntrada           = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorCompraUnitario  = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    QtdSaida             = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorVendaUnitario   = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Quebras              = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CriadoEm             = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesVolumesEmbalados", x => x.Id);
                });

            // ── Veiculos (FK para Motoristas) ──
            migrationBuilder.CreateTable(
                name: "Veiculos",
                columns: table => new
                {
                    Id               = table.Column<int>(type: "integer", nullable: false)
                                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Placa            = table.Column<string>(type: "text", nullable: false),
                    Socio            = table.Column<int>(type: "integer", nullable: false),
                    Ativo            = table.Column<bool>(type: "boolean", nullable: false),
                    MotoristaAtualId = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Veiculos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Veiculos_Motoristas_MotoristaAtualId",
                        column: x => x.MotoristaAtualId,
                        principalTable: "Motoristas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── FK circular: Motoristas → Veiculos ──
            migrationBuilder.AddForeignKey(
                name: "FK_Motoristas_Veiculos_VeiculoPrincipalId",
                table: "Motoristas",
                column: "VeiculoPrincipalId",
                principalTable: "Veiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Viagens (FK → Veiculos, Motoristas) ──
            migrationBuilder.CreateTable(
                name: "Viagens",
                columns: table => new
                {
                    Id                  = table.Column<int>(type: "integer", nullable: false)
                                               .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VeiculoId           = table.Column<int>(type: "integer", nullable: false),
                    MotoristaId         = table.Column<int>(type: "integer", nullable: false),
                    DataSaida           = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataRetorno         = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Destino             = table.Column<string>(type: "text", nullable: true),
                    FreteRecebido       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FreteRetorno        = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Diarias             = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Comissao            = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Adiantamento        = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SaldoAnterior       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Mes                 = table.Column<int>(type: "integer", nullable: false),
                    Ano                 = table.Column<int>(type: "integer", nullable: false),
                    FechamentoRealizado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm            = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Viagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Viagens_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Viagens_Motoristas_MotoristaId",
                        column: x => x.MotoristaId,
                        principalTable: "Motoristas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── CTes (FK → Veiculos, Viagens) ──
            migrationBuilder.CreateTable(
                name: "CTes",
                columns: table => new
                {
                    Id            = table.Column<int>(type: "integer", nullable: false)
                                         .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NumeroCTe     = table.Column<string>(type: "text", nullable: false),
                    ChaveAcesso   = table.Column<string>(type: "text", nullable: false),
                    DataEmissao   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Remetente     = table.Column<string>(type: "text", nullable: true),
                    Destinatario  = table.Column<string>(type: "text", nullable: true),
                    UFOrigem      = table.Column<string>(type: "text", nullable: true),
                    UFDestino     = table.Column<string>(type: "text", nullable: true),
                    CidadeDestino = table.Column<string>(type: "text", nullable: true),
                    ValorNF       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorFrete    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AliquotaICMS  = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PesoKg        = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status        = table.Column<int>(type: "integer", nullable: false),
                    XmlOriginal   = table.Column<string>(type: "text", nullable: true),
                    VeiculoId     = table.Column<int>(type: "integer", nullable: false),
                    ViagemId      = table.Column<int>(type: "integer", nullable: true),
                    NumeroNF      = table.Column<string>(type: "text", nullable: true),
                    ClienteNome   = table.Column<string>(type: "text", nullable: true),
                    CriadoEm      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CTes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CTes_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CTes_Viagens_ViagemId",
                        column: x => x.ViagemId,
                        principalTable: "Viagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── DespesasViagem ──
            migrationBuilder.CreateTable(
                name: "DespesasViagem",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ViagemId    = table.Column<int>(type: "integer", nullable: false),
                    Tipo        = table.Column<int>(type: "integer", nullable: false),
                    Descricao   = table.Column<string>(type: "text", nullable: true),
                    Valor       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Data        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DespesasViagem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DespesasViagem_Viagens_ViagemId",
                        column: x => x.ViagemId,
                        principalTable: "Viagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── AcertosCaminhao ──
            migrationBuilder.CreateTable(
                name: "AcertosCaminhao",
                columns: table => new
                {
                    Id               = table.Column<int>(type: "integer", nullable: false)
                                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VeiculoId        = table.Column<int>(type: "integer", nullable: false),
                    MotoristaId      = table.Column<int>(type: "integer", nullable: false),
                    Mes              = table.Column<int>(type: "integer", nullable: false),
                    Ano              = table.Column<int>(type: "integer", nullable: false),
                    TotalFrete       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalDespesas    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalDiarias     = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalComissao    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAdiantamento= table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SaldoFinal       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CriadoEm         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcertosCaminhao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcertosCaminhao_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcertosCaminhao_Motoristas_MotoristaId",
                        column: x => x.MotoristaId,
                        principalTable: "Motoristas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── FretesRetorno ──
            migrationBuilder.CreateTable(
                name: "FretesRetorno",
                columns: table => new
                {
                    Id              = table.Column<int>(type: "integer", nullable: false)
                                          .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ViagemId        = table.Column<int>(type: "integer", nullable: false),
                    Empresa         = table.Column<string>(type: "text", nullable: true),
                    Valor           = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NumeroDocumento = table.Column<string>(type: "text", nullable: true),
                    TipoPagamento   = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FretesRetorno", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FretesRetorno_Viagens_ViagemId",
                        column: x => x.ViagemId,
                        principalTable: "Viagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── LancamentosBanco ──
            migrationBuilder.CreateTable(
                name: "LancamentosBanco",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Socio       = table.Column<int>(type: "integer", nullable: false),
                    Tipo        = table.Column<int>(type: "integer", nullable: false),
                    Descricao   = table.Column<string>(type: "text", nullable: true),
                    Valor       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Data        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Mes         = table.Column<int>(type: "integer", nullable: false),
                    Ano         = table.Column<int>(type: "integer", nullable: false),
                    VeiculoId   = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosBanco", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LancamentosBanco_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── SaldosReceber ──
            migrationBuilder.CreateTable(
                name: "SaldosReceber",
                columns: table => new
                {
                    Id              = table.Column<int>(type: "integer", nullable: false)
                                          .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Empresa         = table.Column<string>(type: "text", nullable: false),
                    Tipo            = table.Column<string>(type: "text", nullable: true),
                    Valor           = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DataLancamento  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Recebido        = table.Column<bool>(type: "boolean", nullable: false),
                    DataRecebimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NumeroDocumento = table.Column<string>(type: "text", nullable: true),
                    CartaoFrete     = table.Column<int>(type: "integer", nullable: true),
                    VeiculoId       = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaldosReceber", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaldosReceber_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── CartaoFreteTransacoes ──
            migrationBuilder.CreateTable(
                name: "CartaoFreteTransacoes",
                columns: table => new
                {
                    Id              = table.Column<int>(type: "integer", nullable: false)
                                          .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tipo            = table.Column<int>(type: "integer", nullable: false),
                    Empresa         = table.Column<string>(type: "text", nullable: true),
                    Valor           = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Data            = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NumeroDocumento = table.Column<string>(type: "text", nullable: true),
                    Conciliado      = table.Column<bool>(type: "boolean", nullable: false),
                    VeiculoId       = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartaoFreteTransacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CartaoFreteTransacoes_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── Abastecimentos ──
            migrationBuilder.CreateTable(
                name: "Abastecimentos",
                columns: table => new
                {
                    Id          = table.Column<int>(type: "integer", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VeiculoId   = table.Column<int>(type: "integer", nullable: false),
                    PostoId     = table.Column<int>(type: "integer", nullable: false),
                    Data        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Litros      = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorLitro  = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorTotal  = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Mes         = table.Column<int>(type: "integer", nullable: false),
                    Ano         = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm= table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abastecimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Abastecimentos_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Abastecimentos_Postos_PostoId",
                        column: x => x.PostoId,
                        principalTable: "Postos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── RateiosDespesa ──
            migrationBuilder.CreateTable(
                name: "RateiosDespesa",
                columns: table => new
                {
                    Id                  = table.Column<int>(type: "integer", nullable: false)
                                               .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DespesaDivididaId   = table.Column<int>(type: "integer", nullable: false),
                    Socio               = table.Column<int>(type: "integer", nullable: false),
                    Percentual          = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Valor               = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CriadoEm            = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateiosDespesa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RateiosDespesa_DespesasDivididas_DespesaDivididaId",
                        column: x => x.DespesaDivididaId,
                        principalTable: "DespesasDivididas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── SegurosPlaca ──
            migrationBuilder.CreateTable(
                name: "SegurosPlaca",
                columns: table => new
                {
                    Id              = table.Column<int>(type: "integer", nullable: false)
                                          .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SeguroMensalId  = table.Column<int>(type: "integer", nullable: false),
                    VeiculoId       = table.Column<int>(type: "integer", nullable: false),
                    Valor           = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NumeroViagens   = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegurosPlaca", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegurosPlaca_SegurosMensais_SeguroMensalId",
                        column: x => x.SeguroMensalId,
                        principalTable: "SegurosMensais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SegurosPlaca_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── SemPararPlacas ──
            migrationBuilder.CreateTable(
                name: "SemPararPlacas",
                columns: table => new
                {
                    Id               = table.Column<int>(type: "integer", nullable: false)
                                           .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SemPararMensalId = table.Column<int>(type: "integer", nullable: false),
                    VeiculoId        = table.Column<int>(type: "integer", nullable: false),
                    Valor            = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NumeroPassagens  = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemPararPlacas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemPararPlacas_SemPararMensais_SemPararMensalId",
                        column: x => x.SemPararMensalId,
                        principalTable: "SemPararMensais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SemPararPlacas_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── DespesasBarracao ──
            migrationBuilder.CreateTable(
                name: "DespesasBarracao",
                columns: table => new
                {
                    Id               = table.Column<int>(type: "integer", nullable: false)
                                           .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LoteId           = table.Column<int>(type: "integer", nullable: true),
                    Descricao        = table.Column<string>(type: "text", nullable: false),
                    Valor            = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Data             = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Mes              = table.Column<int>(type: "integer", nullable: false),
                    Ano              = table.Column<int>(type: "integer", nullable: false),
                    EhValter      = table.Column<bool>(type: "boolean", nullable: false),
                    HorasTrabalhadas = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ValorHora        = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CriadoEm         = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DespesasBarracao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DespesasBarracao_LotesVolumesEmbalados_LoteId",
                        column: x => x.LoteId,
                        principalTable: "LotesVolumesEmbalados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── MovimentosEstoque ──
            migrationBuilder.CreateTable(
                name: "MovimentosEstoque",
                columns: table => new
                {
                    Id            = table.Column<int>(type: "integer", nullable: false)
                                         .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Produto       = table.Column<int>(type: "integer", nullable: false),
                    Tipo          = table.Column<int>(type: "integer", nullable: false),
                    Quantidade    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Data          = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao     = table.Column<string>(type: "text", nullable: true),
                    ClienteId     = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosEstoque", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentosEstoque_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── Romaneios ──
            migrationBuilder.CreateTable(
                name: "Romaneios",
                columns: table => new
                {
                    Id           = table.Column<int>(type: "integer", nullable: false)
                                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClienteId    = table.Column<int>(type: "integer", nullable: false),
                    DataRomaneio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Mes          = table.Column<int>(type: "integer", nullable: false),
                    Ano          = table.Column<int>(type: "integer", nullable: false),
                    Enviado      = table.Column<bool>(type: "boolean", nullable: false),
                    DataEnvio    = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanalEnvio   = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Romaneios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Romaneios_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── ItensRomaneio ──
            migrationBuilder.CreateTable(
                name: "ItensRomaneio",
                columns: table => new
                {
                    Id             = table.Column<int>(type: "integer", nullable: false)
                                         .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RomaneioId     = table.Column<int>(type: "integer", nullable: false),
                    Produto        = table.Column<int>(type: "integer", nullable: false),
                    Descricao      = table.Column<string>(type: "text", nullable: true),
                    Quantidade     = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Unidade        = table.Column<string>(type: "text", nullable: true),
                    ValorUnitario  = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CriadoEm       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensRomaneio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensRomaneio_Romaneios_RomaneioId",
                        column: x => x.RomaneioId,
                        principalTable: "Romaneios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Boletos ──
            migrationBuilder.CreateTable(
                name: "Boletos",
                columns: table => new
                {
                    Id              = table.Column<int>(type: "integer", nullable: false)
                                          .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClienteId       = table.Column<int>(type: "integer", nullable: true),
                    RomaneioId      = table.Column<int>(type: "integer", nullable: true),
                    Valor           = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DataEmissao     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataVencimento  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status          = table.Column<int>(type: "integer", nullable: false),
                    DataPagamento   = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Descricao       = table.Column<string>(type: "text", nullable: true),
                    NossoNumero     = table.Column<string>(type: "text", nullable: true),
                    TipoProduto     = table.Column<int>(type: "integer", nullable: true),
                    VeiculoId       = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boletos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boletos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Boletos_Romaneios_RomaneioId",
                        column: x => x.RomaneioId,
                        principalTable: "Romaneios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Boletos_Veiculos_VeiculoId",
                        column: x => x.VeiculoId,
                        principalTable: "Veiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── Índices ──
            migrationBuilder.CreateIndex(name: "IX_CTes_VeiculoId",     table: "CTes",            column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_CTes_ViagemId",      table: "CTes",             column: "ViagemId");
            migrationBuilder.CreateIndex(name: "IX_CTes_ChaveAcesso",   table: "CTes",             column: "ChaveAcesso");
            migrationBuilder.CreateIndex(name: "IX_Viagens_VeiculoId",  table: "Viagens",          column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_Viagens_MotoristaId",table: "Viagens",          column: "MotoristaId");
            migrationBuilder.CreateIndex(name: "IX_Veiculos_MotoristaAtualId", table: "Veiculos",  column: "MotoristaAtualId");
            migrationBuilder.CreateIndex(name: "IX_Motoristas_VeiculoPrincipalId", table: "Motoristas", column: "VeiculoPrincipalId");
            migrationBuilder.CreateIndex(name: "IX_Boletos_ClienteId",  table: "Boletos",          column: "ClienteId");
            migrationBuilder.CreateIndex(name: "IX_Boletos_RomaneioId", table: "Boletos",          column: "RomaneioId");
            migrationBuilder.CreateIndex(name: "IX_Boletos_VeiculoId",  table: "Boletos",          column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_Romaneios_ClienteId",table: "Romaneios",        column: "ClienteId");
            migrationBuilder.CreateIndex(name: "IX_ItensRomaneio_RomaneioId", table: "ItensRomaneio", column: "RomaneioId");
            migrationBuilder.CreateIndex(name: "IX_DespesasViagem_ViagemId", table: "DespesasViagem", column: "ViagemId");
            migrationBuilder.CreateIndex(name: "IX_FretesRetorno_ViagemId",  table: "FretesRetorno",  column: "ViagemId");
            migrationBuilder.CreateIndex(name: "IX_AcertosCaminhao_VeiculoId",  table: "AcertosCaminhao", column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_AcertosCaminhao_MotoristaId",table: "AcertosCaminhao", column: "MotoristaId");
            migrationBuilder.CreateIndex(name: "IX_LancamentosBanco_VeiculoId", table: "LancamentosBanco", column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_SaldosReceber_VeiculoId",    table: "SaldosReceber",    column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_CartaoFreteTransacoes_VeiculoId", table: "CartaoFreteTransacoes", column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_Abastecimentos_VeiculoId",   table: "Abastecimentos", column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_Abastecimentos_PostoId",     table: "Abastecimentos", column: "PostoId");
            migrationBuilder.CreateIndex(name: "IX_RateiosDespesa_DespesaDivididaId", table: "RateiosDespesa", column: "DespesaDivididaId");
            migrationBuilder.CreateIndex(name: "IX_SegurosPlaca_SeguroMensalId",table: "SegurosPlaca",  column: "SeguroMensalId");
            migrationBuilder.CreateIndex(name: "IX_SegurosPlaca_VeiculoId",     table: "SegurosPlaca",  column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_SemPararPlacas_SemPararMensalId", table: "SemPararPlacas", column: "SemPararMensalId");
            migrationBuilder.CreateIndex(name: "IX_SemPararPlacas_VeiculoId",   table: "SemPararPlacas",column: "VeiculoId");
            migrationBuilder.CreateIndex(name: "IX_DespesasBarracao_LoteId",    table: "DespesasBarracao", column: "LoteId");
            migrationBuilder.CreateIndex(name: "IX_MovimentosEstoque_ClienteId",table: "MovimentosEstoque", column: "ClienteId");

            // ── Seed data ──
            var seed = new DateTime(2025, 5, 5, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData("Motoristas",
                columns: new[] { "Id","Nome","Apelido","CPF","Telefone","Ativo","VeiculoPrincipalId","SocioPrincipal","CriadoEm","AtualizadoEm" },
                values: new object[,]
                {
                    { 1,  "Paulo Henrique", null, null, null, true, null, 1, seed, seed },
                    { 2,  "Renato",     null, null, null, true, null, 1, seed, seed },
                    { 3,  "Bento",      null, null, null, true, null, 1, seed, seed },
                    { 4,  "Sílvio",     null, null, null, true, null, 2, seed, seed },
                    { 5,  "José Ferraz",null, null, null, true, null, 2, seed, seed },
                    { 6,  "Reinaldo",     null, null, null, true, null, 2, seed, seed },
                    { 7,  "Wellington",     null, null, null, true, null, 2, seed, seed },
                    { 8,  "Caio",        null, null, null, true, null, 2, seed, seed },
                    { 9,  "Marcos",        null, null, null, true, null, 3, seed, seed },
                    { 10, "Rafael",     "Tigre",null, null, true, null, 3, seed, seed },
                });

            migrationBuilder.InsertData("Veiculos",
                columns: new[] { "Id","Placa","Socio","Ativo","MotoristaAtualId","CriadoEm","AtualizadoEm" },
                values: new object[,]
                {
                    {  1, "XAA-9457", 1, true,  1, seed, seed },
                    {  2, "XAB-4C58", 1, true,  2, seed, seed },
                    {  3, "XAC-6D57", 1, true,  3, seed, seed },
                    {  4, "XAD-5H46", 2, true,  4, seed, seed },
                    {  5, "XAE-4D55", 2, true,  5, seed, seed },
                    {  6, "XAF-2J52", 2, true,  6, seed, seed },
                    {  7, "XAG-2G58", 2, true,  7, seed, seed },
                    {  8, "XAH-2I15", 2, true,  8, seed, seed },
                    {  9, "XAJ-2B10", 3, true,  9, seed, seed },
                    { 10, "XAK-3B51", 3, true, 10, seed, seed },
                });

            migrationBuilder.InsertData("Clientes",
                columns: new[] { "Id","Nome","NomeFantasia","CNPJ","Email","Telefone","Cidade","UF","Ativo","CriadoEm","AtualizadoEm" },
                values: new object[,]
                {
                    { 1, "Aurora",          null,               null, null, null, "Anápolis", "GO", true, seed, seed },
                    { 2, "BRZ",              "BRZ - Contato",  null, null, null, null,       null, true, seed, seed },
                    { 3, "CEDRO FRUTAS",      null,               null, null, null, null,       "GO", true, seed, seed },
                    { 4, "Verdurão Dunas",  null,               null, null, null, null,       null, true, seed, seed },
                    { 5, "Girassol",           "Girassol - Horacio",  null, null, null, null,       null, true, seed, seed },
                    { 6, "Litoral",            null,               null, null, null, "Farol",   "GO", true, seed, seed },
                    { 7, "Jatobá",          null,               null, null, null, null,       null, true, seed, seed },
                    { 8, "Novo Dia",         null,               null, null, null, "Palmas",   "TO", true, seed, seed },
                    { 9, "Bem Servido",        null,               null, null, null, "Palmas",   "TO", true, seed, seed },
                });

            migrationBuilder.InsertData("Fazendas",
                columns: new[] { "Id","Nome","Contato","Telefone","Cidade","UF","Ativo","CriadoEm","AtualizadoEm" },
                values: new object[] { 1, "Fazenda Alfa", "Igor", null, "Aldeia", "SP", true, seed, seed });

            migrationBuilder.InsertData("Postos",
                columns: new[] { "Id","Nome","Cidade","UF","Ativo","CriadoEm","AtualizadoEm" },
                values: new object[,]
                {
                    { 1, "Estrada",   null, null, true, seed, seed },
                    { 2, "Trevo",    null, null, true, seed, seed },
                    { 3, "Rodovia",null, null, true, seed, seed },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Boletos");
            migrationBuilder.DropTable(name: "ItensRomaneio");
            migrationBuilder.DropTable(name: "Romaneios");
            migrationBuilder.DropTable(name: "MovimentosEstoque");
            migrationBuilder.DropTable(name: "DespesasBarracao");
            migrationBuilder.DropTable(name: "LotesVolumesEmbalados");
            migrationBuilder.DropTable(name: "RateiosDespesa");
            migrationBuilder.DropTable(name: "DespesasDivididas");
            migrationBuilder.DropTable(name: "SemPararPlacas");
            migrationBuilder.DropTable(name: "SemPararMensais");
            migrationBuilder.DropTable(name: "SegurosPlaca");
            migrationBuilder.DropTable(name: "SegurosMensais");
            migrationBuilder.DropTable(name: "Abastecimentos");
            migrationBuilder.DropTable(name: "Postos");
            migrationBuilder.DropTable(name: "CartaoFreteTransacoes");
            migrationBuilder.DropTable(name: "SaldosReceber");
            migrationBuilder.DropTable(name: "LancamentosBanco");
            migrationBuilder.DropTable(name: "AcertosCaminhao");
            migrationBuilder.DropTable(name: "FretesRetorno");
            migrationBuilder.DropTable(name: "DespesasViagem");
            migrationBuilder.DropTable(name: "CTes");
            migrationBuilder.DropTable(name: "Viagens");

            migrationBuilder.DropForeignKey(
                name: "FK_Motoristas_Veiculos_VeiculoPrincipalId",
                table: "Motoristas");
            migrationBuilder.DropTable(name: "Veiculos");
            migrationBuilder.DropTable(name: "Motoristas");
            migrationBuilder.DropTable(name: "Fazendas");
            migrationBuilder.DropTable(name: "Clientes");
        }
    }
}
