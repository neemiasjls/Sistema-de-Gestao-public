using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;

namespace TransportadoraSystem.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Veiculo> Veiculos => Set<Veiculo>();
    public DbSet<Motorista> Motoristas => Set<Motorista>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Fazenda> Fazendas => Set<Fazenda>();
    public DbSet<Posto> Postos => Set<Posto>();
    public DbSet<CTe> CTes => Set<CTe>();
    public DbSet<Viagem> Viagens => Set<Viagem>();
    public DbSet<DespesaViagem> DespesasViagem => Set<DespesaViagem>();
    public DbSet<AcertoCaminhao> AcertosCaminhao => Set<AcertoCaminhao>();
    public DbSet<FreteRetorno> FretesRetorno => Set<FreteRetorno>();
    public DbSet<Romaneio> Romaneios => Set<Romaneio>();
    public DbSet<ItemRomaneio> ItensRomaneio => Set<ItemRomaneio>();
    public DbSet<Boleto> Boletos => Set<Boleto>();
    public DbSet<LancamentoBanco> LancamentosBanco => Set<LancamentoBanco>();
    public DbSet<SaldoReceber> SaldosReceber => Set<SaldoReceber>();
    public DbSet<CartaoFreteTransacao> CartaoFreteTransacoes => Set<CartaoFreteTransacao>();
    public DbSet<Abastecimento> Abastecimentos => Set<Abastecimento>();
    public DbSet<DespesaDividida> DespesasDivididas => Set<DespesaDividida>();
    public DbSet<RateioDespesa> RateiosDespesa => Set<RateioDespesa>();
    public DbSet<MovimentoEstoque> MovimentosEstoque => Set<MovimentoEstoque>();
    public DbSet<LoteVolumesEmbalados> LotesVolumesEmbalados => Set<LoteVolumesEmbalados>();
    public DbSet<DespesaBarracao> DespesasBarracao => Set<DespesaBarracao>();
    public DbSet<SeguroMensal> SegurosMensais => Set<SeguroMensal>();
    public DbSet<SeguroPlaca> SegurosPlaca => Set<SeguroPlaca>();
    public DbSet<SemPararMensal> SemPararMensais => Set<SemPararMensal>();
    public DbSet<SemPararPlaca> SemPararPlacas => Set<SemPararPlaca>();
    public DbSet<RotaDistancia> RotasDistancia => Set<RotaDistancia>();
    public DbSet<Usuario>       Usuarios       => Set<Usuario>();
    public DbSet<CalculoFreteSalvo> CalculosFreteSalvos => Set<CalculoFreteSalvo>();
    public DbSet<PlanilhaCarga> PlanilhasCarga => Set<PlanilhaCarga>();
    public DbSet<ArquivoNFe> ArquivosNFe => Set<ArquivoNFe>();
    public DbSet<AcertoAlfaContador> AcertoAlfaContadores => Set<AcertoAlfaContador>();
    public DbSet<RomaneioContador> RomaneioContadores => Set<RomaneioContador>();

    // Estoque de produtosA e produtosB
    public DbSet<EstoqueCliente> EstoqueClientes => Set<EstoqueCliente>();
    public DbSet<TipoProdutoB> TiposProdutoB => Set<TipoProdutoB>();
    public DbSet<EstoqueClientePreco> EstoqueClientePrecos => Set<EstoqueClientePreco>();
    public DbSet<CargaProdutoA> CargasProdutoA => Set<CargaProdutoA>();
    public DbSet<VendaProdutoA> VendasProdutoA => Set<VendaProdutoA>();
    public DbSet<ProdutoAVendida> ProdutosAVendidas => Set<ProdutoAVendida>();
    public DbSet<CargaProdutoB> CargasProdutoB => Set<CargaProdutoB>();
    public DbSet<CargaProdutoBItem> CargaProdutoBItens => Set<CargaProdutoBItem>();
    public DbSet<VendaProdutoB> VendasProdutoB => Set<VendaProdutoB>();
    public DbSet<VendaProdutoBItem> VendaProdutoBItens => Set<VendaProdutoBItem>();

    // Tipos de volumes e as escritas de cada um nas notas
    public DbSet<TipoVolume> TiposVolume => Set<TipoVolume>();
    public DbSet<EscritaVolume> EscritasVolume => Set<EscritaVolume>();

    // Acertos semanais com a Fazenda Alfa e os PDFs de cada um
    public DbSet<AcertoAlfa> AcertosAlfa => Set<AcertoAlfa>();
    public DbSet<AcertoAlfaArquivo> AcertoAlfaArquivos => Set<AcertoAlfaArquivo>();

    // Cobranças dos postos: faturas, boletos e as planilhas de divisão geradas
    public DbSet<FaturaPosto> FaturasPosto => Set<FaturaPosto>();
    public DbSet<BoletoPosto> BoletosPosto => Set<BoletoPosto>();
    public DbSet<PlanilhaAbastecimento> PlanilhasAbastecimento => Set<PlanilhaAbastecimento>();

    // Planilhas SG (Seguro / ICMS) do mês, geradas na tela de CT-es ou trazidas do Drive
    public DbSet<PlanilhaSeguro> PlanilhasSeguro => Set<PlanilhaSeguro>();
    public DbSet<AliquotaSeguro> AliquotasSeguro => Set<AliquotaSeguro>();

    // E-mail semanal da Cedro Frutas e do Dunas: destinatários e histórico
    public DbSet<EmailClienteConfig> EmailsClienteConfig => Set<EmailClienteConfig>();
    public DbSet<EmailClienteEnvio> EmailsClienteEnvios => Set<EmailClienteEnvio>();

    // Boletos emitidos para os clientes (a receber) e os avulsos a pagar
    public DbSet<BoletoReceber> BoletosReceber => Set<BoletoReceber>();
    public DbSet<BoletoPagarAvulso> BoletosPagarAvulsos => Set<BoletoPagarAvulso>();
    public DbSet<ConfigEmissaoBoleto> ConfigEmissaoBoleto => Set<ConfigEmissaoBoleto>();

    // Volumes embalados (controle do barracão)
    public DbSet<ControleVolumes> VolumesControles => Set<ControleVolumes>();
    public DbSet<ControleVolumesLinha> VolumesControleLinhas => Set<ControleVolumesLinha>();
    // Romaneios da fazenda lidos dos PDFs do acerto (os sem funrural viram entrada dos volumes)
    public DbSet<RomaneioFazendaLido> RomaneiosFazendaLidos => Set<RomaneioFazendaLido>();

    // Sem Parar: controle de pedágio do mês (no lugar da planilha) e os arquivos de origem
    public DbSet<SemPararControle> SemPararControles => Set<SemPararControle>();
    public DbSet<SemPararLinha> SemPararLinhas => Set<SemPararLinha>();
    public DbSet<SemPararArquivo> SemPararArquivos => Set<SemPararArquivo>();

    // Viagens dos caminhões (acerto mensal por caminhão), motoristas, retornos,
    // tabela de frete por cliente e controle de carregamento
    public DbSet<MotoristaFrota> MotoristasFrota => Set<MotoristaFrota>();
    public DbSet<ViagemCaminhao> ViagensCaminhao => Set<ViagemCaminhao>();
    public DbSet<ViagemCaminhaoVolumes> ViagemCaminhaoVolumes => Set<ViagemCaminhaoVolumes>();
    public DbSet<ViagemCaminhaoAbastecimento> ViagemCaminhaoAbastecimentos => Set<ViagemCaminhaoAbastecimento>();
    public DbSet<ViagemCaminhaoDespesa> ViagemCaminhaoDespesas => Set<ViagemCaminhaoDespesa>();
    public DbSet<ViagemCaminhaoCarga> ViagemCaminhaoCargas => Set<ViagemCaminhaoCarga>();
    public DbSet<RetornoFrete> RetornosFrete => Set<RetornoFrete>();
    public DbSet<PrecoFreteCliente> PrecosFreteCliente => Set<PrecoFreteCliente>();
    public DbSet<CarregamentoVolumes> CarregamentosVolumes => Set<CarregamentoVolumes>();
    public DbSet<QuebraVolumes> QuebrasVolumes => Set<QuebraVolumes>();
    public DbSet<DespesaEmbalados> DespesasEmbalados => Set<DespesaEmbalados>();
    public DbSet<ComissaoEmbalados> ComissoesEmbalados => Set<ComissaoEmbalados>();
    public DbSet<SalarioEmbalados> SalariosEmbalados => Set<SalarioEmbalados>();
    public DbSet<ComissaoBarracao> ComissoesBarracao => Set<ComissaoBarracao>();
    public DbSet<DespesaDoBarracao> DespesasDoBarracao => Set<DespesaDoBarracao>();
    public DbSet<FechamentoMensal> FechamentosMensais => Set<FechamentoMensal>();
    public DbSet<FechamentoDespesa> FechamentoDespesas => Set<FechamentoDespesa>();
    public DbSet<FechamentoCredito> FechamentoCreditos => Set<FechamentoCredito>();
    public DbSet<FechamentoVale> FechamentoVales => Set<FechamentoVale>();
    public DbSet<Consorcio> Consorcios => Set<Consorcio>();
    public DbSet<DespesaDivididaLinha> DespesasDivididasLinhas => Set<DespesaDivididaLinha>();
    public DbSet<DespesaDivididaSocio> DespesasDivididasSocios => Set<DespesaDivididaSocio>();
    public DbSet<DespesaDivididaPlaca> DespesasDivididasPlacas => Set<DespesaDivididaPlaca>();
    public DbSet<PlacaFrota> PlacasFrota => Set<PlacaFrota>();
    public DbSet<MotoristaCnhPdf> MotoristasCnhPdf => Set<MotoristaCnhPdf>();
    // Fretes Agrotrade pela Strada / TIP Bank (PEN-39); tabelas criadas no Ddl.cs
    public DbSet<ViagemAgrotrade> AgrotradeViagens => Set<ViagemAgrotrade>();
    public DbSet<StradaFatura> StradaFaturas => Set<StradaFatura>();
    public DbSet<StradaContrato> StradaContratos => Set<StradaContrato>();
    public DbSet<StradaArquivo> StradaArquivos => Set<StradaArquivo>();
    // Portal das produtosA no celular (EST-09); tabelas criadas no Ddl.cs
    public DbSet<PortalProdutosAHistorico> PortalProdutosAHistorico => Set<PortalProdutosAHistorico>();
    public DbSet<PortalProdutosAConfig> PortalProdutosAConfig => Set<PortalProdutosAConfig>();

    // Data fixa para seed — nunca usar DateTime.UtcNow em seed data (gera migrations não-determinísticas)
    private static readonly DateTime _seedDate = new(2025, 5, 5, 0, 0, 0, DateTimeKind.Utc);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Fechamento do mês: a coluna de ligação no banco é "FechamentoId" (Ddl.cs). Sem isto o EF
        // inventava "FechamentoMensalId" e gravar despesa/crédito/vale dava 42703 (coluna não existe).
        modelBuilder.Entity<FechamentoMensal>().HasMany(f => f.Despesas).WithOne().HasForeignKey(d => d.FechamentoId);
        modelBuilder.Entity<FechamentoMensal>().HasMany(f => f.Creditos).WithOne().HasForeignKey(c => c.FechamentoId);
        modelBuilder.Entity<FechamentoMensal>().HasMany(f => f.Vales).WithOne().HasForeignKey(v => v.FechamentoId);

        // Precisão monetária padrão
        foreach (var property in modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(4);
        }

        // Motoristas seed
        modelBuilder.Entity<Motorista>().HasData(
            new Motorista { Id = 1, Nome = "Paulo Henrique", SocioPrincipal = Socio.Bento, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 2, Nome = "Renato", SocioPrincipal = Socio.Bento, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 3, Nome = "Bento", SocioPrincipal = Socio.Bento, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 4, Nome = "Sílvio", SocioPrincipal = Socio.Caio, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 5, Nome = "José Ferraz", SocioPrincipal = Socio.Caio, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 6, Nome = "Reinaldo", SocioPrincipal = Socio.Caio, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 7, Nome = "Wellington", SocioPrincipal = Socio.Caio, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 8, Nome = "Caio", SocioPrincipal = Socio.Caio, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 9, Nome = "Marcos", SocioPrincipal = Socio.Dudu, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Motorista { Id = 10, Nome = "Rafael", Apelido = "Tigre", SocioPrincipal = Socio.Dudu, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate }
        );

        // Veículos seed — 10 veículos reais da empresa
        modelBuilder.Entity<Veiculo>().HasData(
            // Bento
            new Veiculo { Id = 1, Placa = "XAA-9457", Socio = Socio.Bento, MotoristaAtualId = 1, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Veiculo { Id = 2, Placa = "XAB-4C58", Socio = Socio.Bento, MotoristaAtualId = 2, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Veiculo { Id = 3, Placa = "XAC-6D57", Socio = Socio.Bento, MotoristaAtualId = 3, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            // Caio
            new Veiculo { Id = 4, Placa = "XAD-5H46", Socio = Socio.Caio, MotoristaAtualId = 4, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Veiculo { Id = 5, Placa = "XAE-4D55", Socio = Socio.Caio, MotoristaAtualId = 5, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Veiculo { Id = 6, Placa = "XAF-2J52", Socio = Socio.Caio, MotoristaAtualId = 6, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Veiculo { Id = 7, Placa = "XAG-2G58", Socio = Socio.Caio, MotoristaAtualId = 7, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Veiculo { Id = 8, Placa = "XAH-2I15", Socio = Socio.Caio, MotoristaAtualId = 8, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            // Dudu
            new Veiculo { Id = 9, Placa = "XAJ-2B10", Socio = Socio.Dudu, MotoristaAtualId = 9, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Veiculo { Id = 10, Placa = "XAK-3B51", Socio = Socio.Dudu, MotoristaAtualId = 10, Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate }
        );

        // Clientes seed
        modelBuilder.Entity<Cliente>().HasData(
            new Cliente { Id = 1, Nome = "Aurora", Cidade = "Anápolis", UF = "GO", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 2, Nome = "BRZ", NomeFantasia = "BRZ - Contato", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 3, Nome = "CEDRO FRUTAS", UF = "GO", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 4, Nome = "Verdurão Dunas", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 5, Nome = "Girassol", NomeFantasia = "Girassol - Horacio", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 6, Nome = "Litoral", Cidade = "Farol", UF = "GO", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 7, Nome = "Jatobá", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 8, Nome = "Novo Dia", Cidade = "Palmas", UF = "TO", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Cliente { Id = 9, Nome = "Bem Servido", Cidade = "Palmas", UF = "TO", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate }
        );

        // Fazenda seed
        modelBuilder.Entity<Fazenda>().HasData(
            new Fazenda { Id = 1, Nome = "Fazenda Alfa", Contato = "Igor", Cidade = "Aldeia", UF = "SP", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate }
        );

        // Postos seed
        modelBuilder.Entity<Posto>().HasData(
            new Posto { Id = 1, Nome = "Estrada", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Posto { Id = 2, Nome = "Trevo", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new Posto { Id = 3, Nome = "Rodovia", Ativo = true, CriadoEm = _seedDate, AtualizadoEm = _seedDate }
        );

        // Rotas de distância — todas as rotas do FRETE.py cadastradas no banco
        modelBuilder.Entity<RotaDistancia>().HasData(
            // ALDEIA (SP)
            new RotaDistancia { Id =  1, Origem = "ALDEIA", UFOrigem = "SP", Destino = "ANAPOLIS",               UF = "GO", Km =  774, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  2, Origem = "ALDEIA", UFOrigem = "SP", Destino = "ITUMBIARA",              UF = "GO", Km =  517, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  3, Origem = "ALDEIA", UFOrigem = "SP", Destino = "GOIANIA",                UF = "GO", Km =  722, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  4, Origem = "ALDEIA", UFOrigem = "SP", Destino = "APARECIDA DE GOIANIA",   UF = "GO", Km =  704, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  5, Origem = "ALDEIA", UFOrigem = "SP", Destino = "HIDROLANDIA",            UF = "GO", Km =  685, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  6, Origem = "ALDEIA", UFOrigem = "SP", Destino = "CALDAS NOVAS",           UF = "GO", Km =  642, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  7, Origem = "ALDEIA", UFOrigem = "SP", Destino = "FAROL",                 UF = "GO", Km = 1212, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  8, Origem = "ALDEIA", UFOrigem = "SP", Destino = "SILVANIA",               UF = "GO", Km =  779, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id =  9, Origem = "ALDEIA", UFOrigem = "SP", Destino = "PARAISO DO TOCANTINS",   UF = "TO", Km = 1505, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 10, Origem = "ALDEIA", UFOrigem = "SP", Destino = "PALMAS",                 UF = "TO", Km = 1542, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 11, Origem = "ALDEIA", UFOrigem = "SP", Destino = "BOM JESUS DE GOIAS",     UF = "GO", Km =  582, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 12, Origem = "ALDEIA", UFOrigem = "SP", Destino = "CHAPADA DA NATIVIDADE",  UF = "TO", Km = 1452, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            // RIBEIRA (SP)
            new RotaDistancia { Id = 13, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "ANAPOLIS",               UF = "GO", Km =  812, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 14, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "ITUMBIARA",              UF = "GO", Km =  555, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 15, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "GOIANIA",                UF = "GO", Km =  760, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 16, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "APARECIDA DE GOIANIA",   UF = "GO", Km =  741, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 17, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "HIDROLANDIA",            UF = "GO", Km =  724, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 18, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "CALDAS NOVAS",           UF = "GO", Km =  680, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 19, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "FAROL",                 UF = "GO", Km = 1250, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 20, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "SILVANIA",               UF = "GO", Km =  817, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 21, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "BOM JESUS DE GOIAS",     UF = "GO", Km =  620, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 22, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "PARAISO DO TOCANTINS",   UF = "TO", Km = 1543, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 23, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "PALMAS",                 UF = "TO", Km = 1580, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 24, Origem = "RIBEIRA",  UFOrigem = "SP", Destino = "CHAPADA DA NATIVIDADE",  UF = "TO", Km = 1491, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            // CASCATA (SP)
            new RotaDistancia { Id = 25, Origem = "CASCATA",               UFOrigem = "SP", Destino = "GOIANIA",   UF = "GO", Km =  712, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            new RotaDistancia { Id = 26, Origem = "CASCATA",               UFOrigem = "SP", Destino = "PALMAS",    UF = "TO", Km = 1532, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            // SERRANA (SP)
            new RotaDistancia { Id = 27, Origem = "SERRANA",           UFOrigem = "SP", Destino = "GOIANIA",   UF = "GO", Km =  702, CriadoEm = _seedDate, AtualizadoEm = _seedDate },
            // OUTROS
            new RotaDistancia { Id = 28, Origem = "VILA SUL", UFOrigem = "SP", Destino = "BRASILIA",  UF = "DF", Km =  977, CriadoEm = _seedDate, AtualizadoEm = _seedDate }
        );

        // Motorista.Veiculos — ignorado pois já tratado via Veiculo.MotoristaAtualId
        modelBuilder.Entity<Motorista>().Ignore(m => m.Veiculos);

        // Motorista.VeiculoPrincipalId — FK explícita para evitar ambiguidade
        modelBuilder.Entity<Motorista>()
            .HasOne<Veiculo>()
            .WithMany()
            .HasForeignKey(m => m.VeiculoPrincipalId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Relacionamentos
        modelBuilder.Entity<Veiculo>()
            .HasOne(v => v.MotoristaAtual)
            .WithMany()
            .HasForeignKey(v => v.MotoristaAtualId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CTe>()
            .HasOne(c => c.Veiculo)
            .WithMany(v => v.CTes)
            .HasForeignKey(c => c.VeiculoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CTe>()
            .HasOne(c => c.Viagem)
            .WithMany(v => v.CTes)
            .HasForeignKey(c => c.ViagemId)
            .OnDelete(DeleteBehavior.SetNull);

        // Excluir a fatura só desliga o boleto (a tabela tem ON DELETE SET NULL)
        modelBuilder.Entity<BoletoPosto>()
            .HasOne(b => b.Fatura)
            .WithMany()
            .HasForeignKey(b => b.FaturaId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Viagem>()
            .Ignore(v => v.SaldoFinal);

        modelBuilder.Entity<RetornoFrete>()
            .Ignore(r => r.Desconto)
            .Ignore(r => r.FreteTotal)
            .Ignore(r => r.AdiantamentoDescarga)
            .Ignore(r => r.ReembolsoDescarga)
            .Ignore(r => r.DescargaPaga);
        modelBuilder.Entity<StradaContrato>()
            .HasOne<RetornoFrete>().WithMany()
            .HasForeignKey(c => c.RetornoId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<RetornoFrete>()
            .HasOne(r => r.Viagem).WithMany()
            .HasForeignKey(r => r.ViagemId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ViagemCaminhaoCarga>()
            .HasOne(c => c.Retorno).WithMany()
            .HasForeignKey(c => c.RetornoId)
            .OnDelete(DeleteBehavior.SetNull);
        // Um retorno liga a no máximo uma anotação do motorista (VIA-22); o índice existe no banco (Ddl)
        modelBuilder.Entity<ViagemCaminhaoCarga>()
            .HasIndex(c => c.RetornoId).IsUnique().HasFilter("\"RetornoId\" IS NOT NULL")
            .HasDatabaseName("UQ_ViagemCaminhaoCargas_RetornoId");
        modelBuilder.Entity<ViagemCaminhao>()
            .HasOne(v => v.Motorista).WithMany()
            .HasForeignKey(v => v.MotoristaId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Romaneio>()
            .Ignore(r => r.Total);

        modelBuilder.Entity<ItemRomaneio>()
            .Ignore(i => i.ValorTotal);

        modelBuilder.Entity<LoteVolumesEmbalados>()
            .Ignore(l => l.CustoTotal)
            .Ignore(l => l.ReceitaTotal)
            .Ignore(l => l.Margem);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.AtualizadoEm = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
