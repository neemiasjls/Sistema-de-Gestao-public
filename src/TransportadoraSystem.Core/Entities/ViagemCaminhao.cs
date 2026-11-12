namespace TransportadoraSystem.Core.Entities;

// Controle de viagens dos caminhões, no lugar das planilhas "N-V -MÊS-AAAA.xlsx"
// (uma aba VIAGEMn por viagem) e da folha impressa "RELATORIO DE VIAGEM" que o
// motorista preenchia. Tabelas novas (Ddl.cs), sem mexer nas antigas "Motoristas"
// e "Viagens" do começo do projeto, que têm outro formato.

/// <summary>
/// Motorista dos caminhões. Login = primeiro nome sem acento (nomes parecidos
/// levam o sobrenome: paulohenrique, joseferraz); a senha padrão é login + "123"
/// (decisão do Admin, sem troca obrigatória) e nunca fica guardada em texto.
/// </summary>
public class MotoristaFrota
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    // Como aparece nas planilhas (PAULINHO, J. FERRAZ), separado por vírgula
    public string? Apelidos { get; set; }
    public string Login { get; set; } = "";
    public decimal PorcentagemComissao { get; set; } = 6;
    // Placa habitual (alguns usam sempre a mesma). A viagem sempre pede a placa.
    public string? PlacaCavalo { get; set; }
    public string? PlacaCarreta { get; set; }
    public bool Ativo { get; set; } = true;
    // CNH e documentos (VIA-20): vêm do PDF da CNH digital ou digitados à mão
    public string? Cpf { get; set; }
    public string? Rg { get; set; }
    public string? RgOrgaoUf { get; set; }
    public DateOnly? DataNascimento { get; set; }
    public string? CidadeNascimento { get; set; }
    public string? UfNascimento { get; set; }
    public string? CnhRegistro { get; set; }
    public string? CnhCategoria { get; set; }
    public DateOnly? CnhEmissao { get; set; }
    public DateOnly? CnhValidade { get; set; }
    public string? Pai { get; set; }
    public string? Mae { get; set; }
    // O PDF fica em MotoristasCnhPdf, para as listas não carregarem o arquivo
    public string? CnhPdfNome { get; set; }
    public DateTime? CnhAtualizadaEm { get; set; }
    // Usuário de acesso (perfil motorista) criado para ele
    public int? UsuarioId { get; set; }
    // E-mail do motorista (USU-11); pode ficar vazio. O login dele ainda não usa.
    public string? Email { get; set; }
    // Quando o motorista criou a senha usando a provisória mandada para esse e-mail
    public DateTime? EmailConfirmadoEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Uma viagem: a aba VIAGEMn do acerto mensal do caminhão.</summary>
public class ViagemCaminhao
{
    public int Id { get; set; }
    public int? MotoristaId { get; set; }
    public MotoristaFrota? Motorista { get; set; }
    // Nome como ficou na viagem (importadas trazem o da planilha)
    public string MotoristaNome { get; set; } = "";
    // Placa do cavalo (a cadeia da sobra é por caminhão) e da carreta
    public string Placa { get; set; } = "";
    public string? PlacaCarreta { get; set; }
    public string? Destino { get; set; }
    public DateOnly? DataSaida { get; set; }
    public DateOnly? DataChegada { get; set; }
    public int? KmSaida { get; set; }
    public int? KmChegada { get; set; }
    public decimal? Litros { get; set; }
    // Dinheiro que levou (LEVOU) e quando recebeu
    public decimal Levou { get; set; }
    public DateOnly? DataDinheiro { get; set; }
    // DEVOL. DINHEIRO das planilhas antigas. Desde 07/10/2026 a sobra fica sempre
    // com o motorista (VIA-14): viagem nova grava 0 e a tela não muda; só a
    // importação do acerto antigo traz o valor da planilha (a cadeia de sobras
    // continua igual à planilha).
    public decimal Devolveu { get; set; }
    // Sobra anterior digitada (começo da cadeia ou correção); nulo = sobra da viagem anterior do caminhão
    public decimal? SobraAnteriorInformada { get; set; }
    // Diárias em número (passos de 0,5; meia diária = R$ 50)
    public decimal DiariasQuantidade { get; set; }
    public decimal ValorDiaria { get; set; } = 100;
    public decimal PorcentagemComissao { get; set; } = 6;
    // Mês do acerto em que a viagem entra (a viagem pode sair no fim do mês anterior)
    public int MesReferencia { get; set; }
    public int AnoReferencia { get; set; }
    public string Status { get; set; } = "lancada";      // lancada, conferida, fechada
    public string Origem { get; set; } = "escritorio";   // motorista, escritorio, importacao
    public string? ChaveImportacao { get; set; }
    public string? Observacoes { get; set; }
    public int? CriadoPorUsuarioId { get; set; }
    public DateTime? EnviadoEm { get; set; }
    public DateTime? ConferidoEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public List<ViagemCaminhaoVolumes> Volumes { get; set; } = [];
    public List<ViagemCaminhaoAbastecimento> Abastecimentos { get; set; } = [];
    public List<ViagemCaminhaoDespesa> Despesas { get; set; } = [];
    public List<ViagemCaminhaoCarga> Cargas { get; set; } = [];
}

/// <summary>
/// Cliente da carga de volumes (linhas 9 a 44 da aba): caixas de volumes, de produtoE,
/// de produtoD, produtosB (amarelas e brancas) e produtosC, com os fretes da
/// tabela de preços no dia (VIA-10): mudar a tabela não mexe na viagem lançada.
/// </summary>
public class ViagemCaminhaoVolumes
{
    public int Id { get; set; }
    public int ViagemId { get; set; }
    public ViagemCaminhao? Viagem { get; set; }
    public int Ordem { get; set; }
    public string Cliente { get; set; } = "";
    public decimal CaixasVolumes { get; set; }
    public decimal CaixasProdutoE { get; set; }
    public decimal CaixasProdutoD { get; set; }
    // Pacotes de produtoB (amarelas + brancas). Nas viagens antigas (planilha)
    // é o total sem cor, com amarelas e brancas em zero.
    public decimal ProdutosB { get; set; }
    public decimal ProdutosBAmarelas { get; set; }
    public decimal ProdutosBBrancas { get; set; }
    // Pacotes de 100 produtosC vazias (coluna U do controle de carregamento)
    public decimal ProdutosC { get; set; }
    public decimal ValorCaixa { get; set; } = 9;
    // Frete da caixa de produtoE gravado no dia; nulo = metade da caixa (viagens antigas)
    public decimal? ValorProdutoE { get; set; }
    public decimal ValorProdutoD { get; set; } = 3;
    public decimal ValorProdutoB { get; set; } = 10;
    public decimal ValorProdutoC { get; set; } = 10;
}

/// <summary>
/// Abastecimento da folha. NOTA = a empresa paga (nota/pix, não sai do dinheiro
/// da viagem); PAGO = pago com o dinheiro que o motorista levou.
/// </summary>
public class ViagemCaminhaoAbastecimento
{
    public int Id { get; set; }
    public int ViagemId { get; set; }
    public ViagemCaminhao? Viagem { get; set; }
    public int Ordem { get; set; }
    public DateOnly? Data { get; set; }
    public int? Km { get; set; }
    public decimal? Litros { get; set; }
    public decimal Valor { get; set; }
    public string? Local { get; set; }
    public string? Observacao { get; set; }
    public string Forma { get; set; } = "NOTA";   // NOTA ou PAGO
}

/// <summary>Outra despesa da viagem (carregamento, caixinha, descarga, borracharia, ...).</summary>
public class ViagemCaminhaoDespesa
{
    public int Id { get; set; }
    public int ViagemId { get; set; }
    public ViagemCaminhao? Viagem { get; set; }
    public int Ordem { get; set; }
    public string Tipo { get; set; } = "OUTRA";
    public string? Descricao { get; set; }
    public decimal Valor { get; set; }
    public string? Observacao { get; set; }
}

/// <summary>
/// Carga que o motorista disse que fez: VOLUMES (a carga da fazenda; caixas vêm do
/// controle de carregamento) ou RETORNO (outra carga). Desde 09/11/2026 (VIA-22) a
/// carga RETORNO é só a anotação do motorista: não cria retorno; o sistema a liga
/// sozinho ao retorno importado da planilha (RetornoId), pela placa e pela data.
/// </summary>
public class ViagemCaminhaoCarga
{
    public int Id { get; set; }
    public int ViagemId { get; set; }
    public ViagemCaminhao? Viagem { get; set; }
    public int Ordem { get; set; }
    public string Tipo { get; set; } = "VOLUMES";   // VOLUMES ou RETORNO
    public string? Transportadora { get; set; }
    public DateOnly? Data { get; set; }
    public string? Origem { get; set; }
    public string? Destino { get; set; }
    // O valor que o motorista anotou (não muda com o retorno ligado)
    public decimal Valor { get; set; }
    // Retorno importado ligado a esta anotação (no máximo uma anotação por retorno)
    public int? RetornoId { get; set; }
    public RetornoFrete? Retorno { get; set; }
    // Situação da ligação (só RETORNO): ligado, manual, conferir, sem_par; nulo = ainda não tentou
    public string? Ligacao { get; set; }
    // Por que ficou para conferir ou sem par (texto da tela)
    public string? LigacaoAviso { get; set; }
}

/// <summary>
/// Frete de retorno: a linha da planilha "CONTROLE MENSAL DE RETORNOS".
/// DESCONT (H) = (VALOR FRETE + ADICIONAL − PEDÁGIO) / 100 × PORC. TIP, salvo
/// quando foi digitado à mão; FRETE TOTAL (J) = D + F − E − H − G.
/// </summary>
public class RetornoFrete
{
    public int Id { get; set; }
    public DateOnly Data { get; set; }
    public string? Destino { get; set; }
    public string? Transportadora { get; set; }
    public decimal ValorFrete { get; set; }
    public decimal Pedagio { get; set; }
    public decimal Adicional { get; set; }
    public decimal DescontoSeguro { get; set; }
    public decimal? PorcentagemTip { get; set; }
    public decimal? DescontoManual { get; set; }
    public string? Motorista { get; set; }
    public string? Placa { get; set; }
    // OBSERVAÇÕES da planilha: em geral a cidade de origem
    public string? Observacoes { get; set; }
    public string? FormaPagamento { get; set; }
    public int? ViagemId { get; set; }
    public ViagemCaminhao? Viagem { get; set; }
    public int? MotoristaId { get; set; }
    // % de ICMS do frete (coluna PORCENT. da aba da viagem)
    public decimal PorcentagemIcms { get; set; }
    // Retorno que só aparece na viagem (mudança, frete avulso): fora do total mensal
    public bool ForaDoControle { get; set; }
    public string Origem { get; set; } = "escritorio";   // escritorio, motorista, importacao
    public bool Conferido { get; set; } = true;
    public string? ChaveImportacao { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
    // VIA-27: descarga paga (digitada; nulo = não lançada) e o contrato da Strada ligado sozinho
    public decimal? Descarga { get; set; }
    public string? Contrato { get; set; }
    // Cópia do "Valor Contrato" do PDF (valor cheio, antes do seguro)
    public decimal? ValorContrato { get; set; }
    public string? ContratoLigacao { get; set; }   // ligado, manual, conferir, sem_par, solto
    public string? ContratoAviso { get; set; }
    public string? ViagemLigacao { get; set; }     // auto, manual, solto (VIA-25)

    /// <summary>Contrato (PDF) − frete contratado; nulo sem contrato.</summary>
    public decimal? AdiantamentoDescarga => ValorContrato is decimal c ? c - ValorFrete : null;

    /// <summary>
    /// A descarga é o ADICIONAL (coluna F da planilha de retornos, Admin 10/11/2026); a coluna Descarga só vale
    /// quando digitada à parte. Nulo = sem descarga lançada.
    /// </summary>
    public decimal? DescargaPaga => Descarga ?? (Adicional > 0 ? Adicional : null);

    /// <summary>Descarga − adiantamento, nunca abaixo de 0; nulo sem descarga, sem contrato ou com adiantamento negativo.</summary>
    public decimal? ReembolsoDescarga => DescargaPaga is decimal d && AdiantamentoDescarga is decimal a && a >= 0
        ? Math.Max(0, d - a) : null;

    public decimal Desconto => DescontoManual ?? Math.Round((ValorFrete + Adicional - Pedagio) / 100m * (PorcentagemTip ?? 0), 4);
    public decimal FreteTotal => ValorFrete + Adicional - Pedagio - Desconto - DescontoSeguro;
}

/// <summary>
/// Frete por cliente de volumes (painel de preços, VIA-10): caixa normal, produtoE e
/// produtoD. O frete da produtoB e o da produtoC moram no cadastro do estoque
/// (EstoqueCliente.FreteProdutoB/FreteProdutoC), ligado por EstoqueClienteId:
/// a viagem e a venda leem o mesmo valor. ValorProdutoB ficou sem uso.
/// </summary>
public class PrecoFreteCliente
{
    public int Id { get; set; }
    public string Cliente { get; set; } = "";
    // Outros nomes do mesmo cliente no controle de carregamento (COM. OMEGA, VINHEDO), por vírgula
    public string? Apelidos { get; set; }
    public decimal ValorCaixa { get; set; } = 9;
    // Gravada com o valor real (metade da caixa na carga inicial: 9 → 4,50)
    public decimal? ValorProdutoE { get; set; }
    public decimal ValorProdutoD { get; set; } = 3;
    public decimal ValorProdutoB { get; set; } = 10;
    // O mesmo cliente no cadastro do estoque (preços de venda e frete de produtoB e produtoC)
    public int? EstoqueClienteId { get; set; }
    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>ProdutoE gravada; sem valor, metade da caixa.</summary>
    public decimal ProdutoE => ValorProdutoE ?? Math.Round(ValorCaixa / 2m, 2);
}

/// <summary>
/// Controle de carregamento (mapa da carga de volumes) importado de planilha: a
/// carga é identificada por placa + dia e casa com a viagem.
/// </summary>
public class CarregamentoVolumes
{
    public int Id { get; set; }
    public DateOnly Data { get; set; }
    public string Placa { get; set; } = "";
    public string? Motorista { get; set; }
    public string? Destino { get; set; }
    public string? Observacao { get; set; }
    public string? NomeArquivo { get; set; }
    // [{cliente, volumes, produtoE, produtoD, amarelas, brancas, produtosC, colunas:{JB:..}}]
    public string ItensJson { get; set; } = "[]";
    public int? ViagemId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>PDF da CNH do motorista, separado do cadastro (uma linha por motorista).</summary>
public class MotoristaCnhPdf
{
    [System.ComponentModel.DataAnnotations.Key, System.ComponentModel.DataAnnotations.Schema.DatabaseGenerated(System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.None)]
    public int MotoristaId { get; set; }
    public byte[] Pdf { get; set; } = [];
}
