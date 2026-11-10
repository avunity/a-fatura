using System.Text;
using System.Text.Json.Serialization;
using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Features.Accountant;
using AFatura.Api.Features.Auth;
using AFatura.Api.Features.Backoffice;
using AFatura.Api.Features.Banking;
using AFatura.Api.Features.Classification;
using AFatura.Api.Features.EFatura;
using AFatura.Api.Features.Extraction;
using AFatura.Api.Features.Invoices;
using AFatura.Api.Features.Mail;
using AFatura.Api.Features.Messages;
using AFatura.Api.Features.Organizations;
using AFatura.Api.Features.Reminders;
using AFatura.Api.Features.Vehicles;
using AFatura.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Windows-1252 CSVs (AT, banks)

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ── Options ──
builder.Services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
builder.Services.Configure<StorageOptions>(cfg.GetSection("Storage"));
builder.Services.Configure<AiOptions>(cfg.GetSection("Ai"));
builder.Services.Configure<MailOptions>(cfg.GetSection("Mail"));

var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key must have at least 32 characters (set JWT__KEY).");

// ── Infrastructure ──
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Default")));
builder.Services.AddHttpContextAccessor();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 100 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 100 * 1024 * 1024);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorizationBuilder().AddPolicy("Admin", p => p.RequireRole("Admin"));

// ── Application services ──
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<FileStorage>();
builder.Services.AddScoped<ClassificationService>();
builder.Services.AddScoped<EFaturaMatcher>();
builder.Services.AddScoped<InvoiceProcessor>();
builder.Services.AddScoped<InvoiceIntake>();
builder.Services.AddScoped<InboundMailService>();
builder.Services.AddSingleton<QrDecoder>();
builder.Services.AddSingleton<InvoiceQueue>();
builder.Services.AddHostedService<InvoiceWorker>();
builder.Services.AddHostedService<ImapPollingWorker>();
builder.Services.AddSingleton<AFaturaMessageStore>();
builder.Services.AddHostedService<SmtpReceiverWorker>();
builder.Services.AddHostedService<ReminderWorker>();

switch (cfg["Ai:Provider"]?.ToLowerInvariant())
{
    case "anthropic":
        builder.Services.AddHttpClient<IAiExtractor, AnthropicApiExtractor>(c => c.Timeout = TimeSpan.FromMinutes(3));
        break;
    case "none":
        builder.Services.AddSingleton<IAiExtractor, NoAiExtractor>();
        break;
    default:
        builder.Services.AddSingleton<IAiExtractor, ClaudeCliExtractor>();
        break;
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = ex switch
    {
        AppException a => (a.Status, a.Message),
        BadHttpRequestException b => (b.StatusCode, "Pedido inválido"),
        DbUpdateException => (409, "Conflito ao gravar os dados"),
        _ => (500, "Erro interno")
    };
    if (status >= 500) app.Logger.LogError(ex, "Unhandled error on {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { status, title });
}));
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapInvoiceEndpoints();
app.MapMasterDataEndpoints();
app.MapBankEndpoints();
app.MapEFaturaEndpoints();
app.MapMailEndpoints();
app.MapMessageEndpoints();
app.MapAccountantEndpoints();
app.MapBackofficeEndpoints();

await Seed.RunAsync(app.Services);
app.Run();
