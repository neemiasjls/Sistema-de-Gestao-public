using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// ── Limite de tentativas de login ──────────────────────────────────
// O sistema fica exposto na internet: sem isso, um robô pode testar
// milhares de senhas por minuto. 5 tentativas por minuto, por IP.
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opt.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    opt.OnRejected = async (ctx, _) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            erro = "Muitas tentativas de login. Aguarde um minuto e tente de novo."
        });
    };
});

// ── Banco de dados ─────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
       .ConfigureWarnings(w => w.Ignore(
           Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning)));

// ── Serviços ───────────────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<StradaService>();
builder.Services.AddHttpClient<EmailService>();
// Sicoob: o certificado A1 vai na conexão (mTLS), por isso o handler próprio
// 60 s: a emissão do boleto devolve o PDF junto e demora mais que a consulta
builder.Services.AddHttpClient<SicoobService>(c => c.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(sp => SicoobService.CriarHandler(
        sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger("Sicoob")))
    .SetHandlerLifetime(TimeSpan.FromHours(6));
builder.Services.AddScoped<CTeImportService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<NFeImportService>();
builder.Services.AddScoped<CTeExcelService>();
builder.Services.AddScoped<SemPararFechamentoService>();
builder.Services.AddScoped<SemPararPlanilhaService>();
builder.Services.AddScoped<AbastecimentoFechamentoService>();
builder.Services.AddScoped<AcertoAlfaService>();
builder.Services.AddScoped<ConferenciaSemanaService>();
builder.Services.AddScoped<RomaneioEstoqueService>();
builder.Services.AddScoped<ImportacaoEstoqueService>();
builder.Services.AddScoped<ArquivoNFeService>();
builder.Services.AddScoped<TiposVolumeService>();
builder.Services.AddScoped<PlanilhaSeguroService>();
builder.Services.AddScoped<EmailClienteService>();
builder.Services.AddScoped<BoletoMontagemService>();
builder.Services.AddScoped<PostosPagosExtratoService>();
builder.Services.AddScoped<BoletoEmissaoService>();
builder.Services.AddScoped<BoletoReceberImportacao>();
builder.Services.AddScoped<RomaneiosEmbalarService>();
builder.Services.AddScoped<ViagensCaminhaoService>();
builder.Services.AddScoped<FechamentoMensalService>();
builder.Services.AddScoped<EstoqueVendasService>();
builder.Services.AddScoped<PrecosClienteService>();
builder.Services.AddScoped<EmbaladosService>();
builder.Services.AddScoped<BarracaoService>();
builder.Services.AddScoped<TransportadoraSystem.API.Controllers.CobrancasAbastecimentoGuarda>();

// ── JWT ────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException(
        "JWT:Secret não configurado. Defina em appsettings.Development.json/appsettings.Production.json (não commitados).");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer           = false,
            ValidateAudience         = false,
            ClockSkew                = TimeSpan.Zero,
        };
        // Retorna JSON em vez de redirect para 401/403
        opt.Events = new JwtBearerEvents
        {
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode  = 401;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsJsonAsync(new { erro = "Não autenticado. Faça login para continuar." });
            },
            OnForbidden = ctx =>
            {
                ctx.Response.StatusCode  = 403;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsJsonAsync(new { erro = "Sem permissão para acessar este recurso." });
            },
        };
    });

builder.Services.AddAuthorization();

// ── CORS ───────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration["CORS:AllowedOrigins"]?.Split(',') ?? ["http://localhost:5174"];

// Cabeçalhos que o JavaScript do site precisa conseguir LER na resposta.
// Sem isso o navegador esconde o cabeçalho e o download sai com nome errado.
// Não adianta o controller setar Access-Control-Expose-Headers na mão: o
// middleware de CORS roda num OnStarting, depois do controller, e sobrescreve.
string[] exposedHeaders =
[
    "Content-Disposition",                      // nome do arquivo baixado
    "X-Filename",                               // idem, na exportação de CT-e
    "X-Avisos",                                 // avisos da geração
    "X-Soma", "X-Qtde", "X-Total-Fatura",       // conferência do fechamento de abastecimento
];

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
            p.SetIsOriginAllowed(origin => new Uri(origin).Host is "localhost" or "127.0.0.1")
             .AllowAnyMethod()
             .AllowAnyHeader()
             .WithExposedHeaders(exposedHeaders);
        else
            p.WithOrigins(allowedOrigins)
             .AllowAnyMethod()
             .AllowAnyHeader()
             .WithExposedHeaders(exposedHeaders);
    }));

// ── Controllers ────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Transportadora API", Version = "v1" });
    // Suporte a JWT no Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name   = "Authorization",
        Type   = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        In     = Microsoft.OpenApi.Models.ParameterLocation.Header,
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── Build ──────────────────────────────────────────────────────────
var app = builder.Build();

// ── Handler global de exceções ────────────────────────────────────
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex  = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var log = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    log.LogError(ex, "Exceção não tratada em {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
    ctx.Response.StatusCode  = 500;
    ctx.Response.ContentType = "application/json";
    var msg = app.Environment.IsDevelopment() ? ex?.Message ?? "Erro interno" : "Erro interno do servidor.";
    await ctx.Response.WriteAsJsonAsync(new { erro = msg });
}));

// ── Garante tabela de cálculos de frete salvos (sem migration) ─────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "CalculosFreteSalvos" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                "Placa" text NOT NULL,
                "ValorCarga" numeric(18,4) NOT NULL,
                "ValorPedagio" numeric(18,4) NOT NULL,
                "PesoTotal" numeric(18,4) NOT NULL,
                "LiquotaBruta" numeric(18,4) NOT NULL,
                "LiquotaArredondada" numeric(18,4) NOT NULL,
                "TotalNFs" integer NOT NULL,
                "TotalCTes" integer NOT NULL,
                "PayloadJson" text NOT NULL,
                "CriadoEm" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_CalculosFreteSalvos" PRIMARY KEY ("Id")
            );
            """);
        db.Database.ExecuteSqlRaw("""
            ALTER TABLE IF EXISTS "AcertoAlfaContador" RENAME TO "AcertoAlfaContadores";
            CREATE TABLE IF NOT EXISTS "AcertoAlfaContadores" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                "Ultimo" integer NOT NULL,
                CONSTRAINT "PK_AcertoAlfaContadores" PRIMARY KEY ("Id")
            );
            INSERT INTO "AcertoAlfaContadores" ("Ultimo")
            SELECT 184 WHERE NOT EXISTS (SELECT 1 FROM "AcertoAlfaContadores");
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "RomaneioContadores" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                "Apelido" text NOT NULL UNIQUE,
                "Ultimo" integer NOT NULL,
                CONSTRAINT "PK_RomaneioContadores" PRIMARY KEY ("Id")
            );
            INSERT INTO "RomaneioContadores" ("Apelido", "Ultimo")
            SELECT v.a, v.u FROM (VALUES
                ('AURORA', 164), ('BRZ', 155), ('CEDRO FRUTAS', 164),
                ('DUNAS', 137), ('FAROL', 165)
            ) AS v(a, u)
            WHERE NOT EXISTS (SELECT 1 FROM "RomaneioContadores" rc WHERE rc."Apelido" = v.a);
            """);
        // Estoque, planilhas da carga e arquivo das notas.
        // Um comando por vez: junto num bloco só, uma instrução com problema
        // derrubava a criação de todas as outras tabelas, sem aparecer em lugar
        // nenhum.
        foreach (var comando in Ddl.NovasTabelas)
        {
            try { db.Database.ExecuteSqlRaw(comando); }
            catch (Exception ex)
            {
                var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var inicio = comando.TrimStart().Split('\n')[0].Trim();
                log.LogError(ex, "DDL falhou em: {Inicio}", inicio);
            }
        }
    }
    catch (Exception ex)
    {
        var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        log.LogWarning("Criação da tabela CalculosFreteSalvos ignorada: {Msg}", ex.Message);
    }
}

// ── Seed do usuário admin e do dono ───────────────────────────────
// Depois do DDL: precisa das colunas Dono e TrocarSenha já criadas.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var authSvc = scope.ServiceProvider.GetRequiredService<AuthService>();
        await authSvc.SeedAdminAsync();
        // Acesso dos motoristas cadastrados (perfil motorista, senha padrão).
        // Login que já é de alguém do escritório não é tocado: vira aviso no log.
        foreach (var aviso in await authSvc.GarantirUsuariosMotoristasAsync())
            scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogWarning("Motorista sem acesso: {Aviso}", aviso);
    }
    catch (Exception ex)
    {
        var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        log.LogWarning("Seed de admin ignorado: {Msg}", ex.Message);
    }
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Senha provisória: enquanto a pessoa não criar a própria, a API só atende o
// "quem sou eu" e a troca de senha. A tela também bloqueia, mas quem garante é
// aqui — a tela dá para contornar.
app.Use(async (ctx, next) =>
{
    var provisoria = ctx.User.FindFirst(AuthService.ClaimTrocarSenha)?.Value == "true";
    var path = ctx.Request.Path.Value?.ToLowerInvariant() ?? "";
    if (provisoria && path.StartsWith("/api/") && path != "/api/auth/me" && path != "/api/auth/trocar-senha")
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { erro = "Crie a sua senha antes de continuar.", trocarSenha = true });
        return;
    }
    await next();
});

// Perfil motorista: só o painel dele (a folha da viagem) e o "quem sou eu". O resto
// da API responde 403 — a tela também esconde, mas quem garante é aqui.
app.Use(async (ctx, next) =>
{
    var motorista = ctx.User.FindFirst(AuthService.ClaimPerfil)?.Value == AuthService.PerfilMotorista;
    var path = ctx.Request.Path.Value?.ToLowerInvariant() ?? "";
    if (motorista && path.StartsWith("/api/") && path != "/api/auth/me" && !path.StartsWith("/api/motorista-painel/"))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { erro = "Acesso só ao painel do motorista.", motorista = true });
        return;
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

// Diz se as tabelas criadas na subida existem mesmo. Sem nome de tabela, só a
// conta — serve para saber, de fora, se o banco subiu completo.
app.MapGet("/health/tabelas", async (AppDbContext db) =>
{
    string[] esperadas =
    [
        "EstoqueClientes", "TiposProdutoB", "EstoqueClientePrecos",
        "CargasProdutoA", "VendasProdutoA", "ProdutosAVendidas",
        "CargasProdutoB", "CargaProdutoBItens", "VendasProdutoB",
        "VendaProdutoBItens", "PlanilhasCarga", "ArquivosNFe",
        "TiposVolume", "EscritasVolume", "AcertosAlfa", "AcertoAlfaArquivos",
        "FaturasPosto", "BoletosPosto", "PlanilhasAbastecimento",
        "PlanilhasSeguro",
        "EmailsClienteConfig", "EmailsClienteEnvios", "BoletosReceber", "BoletosPagarAvulsos",
        "VolumesControles", "VolumesControleLinhas", "RomaneiosFazendaLidos",
        "SemPararControles", "SemPararLinhas", "SemPararArquivos",
        "MotoristasFrota", "ViagensCaminhao", "ViagemCaminhaoVolumes", "ViagemCaminhaoAbastecimentos",
        "ViagemCaminhaoDespesas", "ViagemCaminhaoCargas", "RetornosFrete", "PrecosFreteCliente", "CarregamentosVolumes",
        "QuebrasVolumes", "DespesasEmbalados", "ComissoesEmbalados", "SalariosEmbalados",
        "ComissoesBarracao", "DespesasDoBarracao",
        "FechamentosMensais", "FechamentoDespesas", "FechamentoCreditos", "FechamentoVales", "Consorcios",
        "DespesasDivididasLinhas", "DespesasDivididasSocios", "DespesasDivididasPlacas", "PlacasFrota", "MotoristasCnhPdf",
        "AgrotradeViagens", "StradaFaturas", "StradaContratos", "StradaArquivos",
        "ConfigEmissaoBoleto",
    ];

    try
    {
        int faltando = 0;
        foreach (var tabela in esperadas)
        {
            var nome = $"\"{tabela}\"";
            var achou = await db.Database
                .SqlQuery<string?>($"SELECT to_regclass({nome})::text AS \"Value\"")
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(achou)) faltando++;
        }
        // Colunas acrescentadas em tabela que já existia: ALTER que falhou não
        // aparece na conta das tabelas (Ddl.ColunasNovas)
        int colunasFaltando = 0;
        foreach (var (tabela, coluna) in Ddl.ColunasNovas)
        {
            var n = await db.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = {tabela} AND column_name = {coluna}")
                .FirstOrDefaultAsync();
            if (n == 0) colunasFaltando++;
        }
        return Results.Ok(new
        {
            ok = faltando == 0 && colunasFaltando == 0, esperadas = esperadas.Length, faltando,
            colunasEsperadas = Ddl.ColunasNovas.Length, colunasFaltando,
        });
    }
    catch (Exception)
    {
        return Results.Ok(new { ok = false, esperadas = esperadas.Length, faltando = -1 });
    }
}).AllowAnonymous();


app.MapControllers();
app.Run();
