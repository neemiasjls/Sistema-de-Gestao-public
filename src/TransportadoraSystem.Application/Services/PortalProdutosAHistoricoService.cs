using System.Text.Json;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Grava o histórico do portal das produtosA (EST-09), que só o dono vê. Ações:
/// usuario_cadastrado, senha_esquecida, cliente_cadastrado, anotacao_criada, anotacao_editada,
/// anotacao_excluida, anotacao_excluida_no_sistema, codigo_trocado.
/// "Antes" e "Depois" vão como JSON em texto.
/// </summary>
public class PortalProdutosAHistoricoService(AppDbContext db)
{
    public const string UsuarioCadastrado          = "usuario_cadastrado";
    public const string SenhaEsquecida             = "senha_esquecida";
    public const string ClienteCadastrado          = "cliente_cadastrado";
    public const string AnotacaoCriada             = "anotacao_criada";
    public const string AnotacaoEditada            = "anotacao_editada";
    public const string AnotacaoExcluida           = "anotacao_excluida";
    public const string AnotacaoExcluidaNoSistema  = "anotacao_excluida_no_sistema";
    public const string CodigoTrocado              = "codigo_trocado";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Põe a linha no contexto sem salvar (vai junto com o SaveChanges de quem chamou).</summary>
    public PortalProdutosAHistorico Adicionar(int? usuarioId, string usuarioNome, string acao, string resumo,
        int? vendaProdutoAId = null, object? antes = null, object? depois = null, string? ip = null)
    {
        var linha = new PortalProdutosAHistorico
        {
            Quando        = DateTime.UtcNow,
            UsuarioId     = usuarioId,
            UsuarioNome   = usuarioNome,
            Acao          = acao,
            VendaProdutoAId = vendaProdutoAId,
            Resumo        = resumo,
            Antes         = antes == null ? null : JsonSerializer.Serialize(antes, Json),
            Depois        = depois == null ? null : JsonSerializer.Serialize(depois, Json),
            Ip            = ip,
        };
        db.PortalProdutosAHistorico.Add(linha);
        return linha;
    }

    /// <summary>Grava a linha na hora.</summary>
    public async Task RegistrarAsync(int? usuarioId, string usuarioNome, string acao, string resumo,
        int? vendaProdutoAId = null, object? antes = null, object? depois = null, string? ip = null)
    {
        Adicionar(usuarioId, usuarioNome, acao, resumo, vendaProdutoAId, antes, depois, ip);
        await db.SaveChangesAsync();
    }
}
