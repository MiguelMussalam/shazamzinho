// Shazamzin.Api — ASP.NET Core 10 Minimal API
//
// POST /match recebe o payload binário do ESP (docs/protocol.md), decodifica, chama
// IMatchingService e devolve JSON em snake_case. API key obrigatória em /match; /health aberto.
// Fase 2: o matching real (Valkey + SQLite) é o padrão; Matching:Provider=Fake mantém o da Fase 1.

using System.Text.Json;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;
using Shazamzin.Api.Middleware;
using Shazamzin.Api.OpenApi;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Interfaces;
using Shazamzin.Core.Models;
using Shazamzin.Core.Matching;
using Shazamzin.Infrastructure;
using Shazamzin.Infrastructure.Matching;
using Shazamzin.Infrastructure.Sqlite;
using Shazamzin.Infrastructure.Valkey;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("logs/shazamzin-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<ApiDocumentTransformer>());

// Matching:Provider = "Valkey" (padrão, matching real) ou "Fake" (sem infraestrutura: testes e demos do contrato).
var matchingOptions = builder.Configuration.GetSection(MatchingOptions.SectionName).Get<MatchingOptions>() ?? new MatchingOptions();
var provider = builder.Configuration[$"{MatchingOptions.SectionName}:Provider"] ?? "Valkey";

if (provider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IMatchingService, FakeMatchingService>();
    builder.Services.AddSingleton<IHealthProbe, DisabledHealthProbe>();
}
else
{
    var sqliteConnectionString = SqliteInitializer.ResolveConnectionString(
        builder.Configuration["Sqlite:ConnectionString"] ?? "Data Source=./data/shazamzin.db",
        AppContext.BaseDirectory);
    SqliteInitializer.EnsureCreated(sqliteConnectionString);

    // abortConnect=false: a API sobe mesmo com o Valkey fora do ar e reconecta sozinha; /health mostra "degraded".
    var valkeyOptions = ConfigurationOptions.Parse(builder.Configuration["Valkey:ConnectionString"] ?? "localhost:6379");
    valkeyOptions.AbortOnConnectFail = false;
    valkeyOptions.ConnectTimeout = 3000;
    valkeyOptions.SyncTimeout = 2000;
    valkeyOptions.AsyncTimeout = 2000;

    builder.Services.AddSingleton(matchingOptions);
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(valkeyOptions));
    builder.Services.AddSingleton<ISongRepository>(_ => new SqliteSongRepository(sqliteConnectionString));
    builder.Services.AddSingleton<IMatchingService, ValkeyMatchingService>();
    builder.Services.AddSingleton<IHealthProbe, ValkeyHealthProbe>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
    app.MapScalarApiReference("/docs", options => options
        .WithTitle("Shazamzin API")
        .AddPreferredSecuritySchemes("ApiKey")
        .DisableAgent()
        .DisableDefaultFonts());
}

// Enriquece todos os logs da requisição com RequestId e DeviceId.
app.Use(async (context, next) =>
{
    var deviceId = context.Request.Headers["X-Device-ID"].ToString();
    using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
    using (LogContext.PushProperty("DeviceId", string.IsNullOrWhiteSpace(deviceId) ? "-" : deviceId))
    {
        await next();
    }
});
app.UseSerilogRequestLogging();
app.UseMiddleware<ApiKeyAuthMiddleware>();

// Health check: PING no Valkey + COUNT no SQLite. 200 se tudo up, 503 se alguma dependência caiu.
app.MapGet("/health", async (IHealthProbe probe, CancellationToken ct) =>
{
    var report = await probe.CheckAsync(ct);
    return Results.Json(report, statusCode: report.IsHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
})
.WithName("Health")
.WithSummary("Verifica a saúde da API e do catálogo")
.WithDescription("Endpoint público. Verifica Valkey e SQLite e informa o total de músicas indexadas. " +
    "Retorna 503 quando alguma dependência está indisponível. No provider Fake, as dependências aparecem como disabled.")
.Produces<HealthReport>(StatusCodes.Status200OK)
.Produces<HealthReport>(StatusCodes.Status503ServiceUnavailable);

app.MapPost("/match", async (HttpContext context, IMatchingService matching, ILogger<Program> logger, CancellationToken ct) =>
{
    if (context.Request.ContentType is not { } contentType ||
        !contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(new { error = "Content-Type deve ser application/octet-stream." },
            statusCode: StatusCodes.Status415UnsupportedMediaType);
    }

    // Lê no máximo MaxPayloadSize + 1 bytes: se passar disso, o corpo é grande demais.
    using var body = new MemoryStream();
    var buffer = new byte[16 * 1024];
    int read;
    while ((read = await context.Request.Body.ReadAsync(buffer, ct)) > 0)
    {
        body.Write(buffer, 0, read);
        if (body.Length > FingerprintCodec.MaxPayloadSize)
        {
            return Results.Json(new { error = $"Payload maior que {FingerprintCodec.MaxPayloadSize} bytes." },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
    }

    if (!FingerprintCodec.TryDecode(body.GetBuffer().AsSpan(0, (int)body.Length), out var payload, out var error))
    {
        logger.LogWarning("Payload inválido: {Error}", error);
        return Results.BadRequest(new { error });
    }

    var result = await matching.MatchAsync(payload!, ct);

    logger.LogInformation(
        "Match session={SessionId} hashes={Hashes} match={Match} song={SongId} {ElapsedMs}ms",
        payload!.SessionId, payload.Fingerprints.Count, result.Match, result.SongId, result.ProcessingTimeMs);

    return Results.Ok(result);
})
.WithName("Match")
.WithSummary("Identifica uma música pelos fingerprints acústicos")
.WithDescription("""
    Envie um arquivo `.bin` como corpo **application/octet-stream**, com bytes brutos (sem JSON ou base64).
    Informe a chave configurada em `Auth:ApiKey` no campo de autenticação **ApiKey** (`X-Api-Key`).
    O header opcional `X-Device-ID` identifica o dispositivo nos logs.

    Todos os inteiros usam **little-endian**. O payload tem `8 + 8 × count` bytes (máximo: 524.288 bytes):

    | Offset | Tipo | Campo |
    | --- | --- | --- |
    | 0 | uint8 | protocol_version = 1 |
    | 1 | uint8 | reserved = 0 |
    | 2 | uint16 | count (0 a 65535) |
    | 4 | uint32 | session_id |
    | 8 + 8 × i | uint32 | hash do fingerprint i |
    | 12 + 8 × i | uint32 | timestamp_ms do fingerprint i |

    `count = 0` é válido e resulta em `match: false`. Uma resposta 200 pode representar match ou no-match;
    consulte o campo `match`. Campos nulos são omitidos da resposta JSON.

    Para um teste sem fingerprints, crie um arquivo com os bytes hexadecimais `01 00 00 00 01 00 00 00`.
    Consulte `docs/protocol.md` e `scripts/send-test-payload.ps1` no repositório para outros exemplos.
    """)
.Accepts<byte[]>("application/octet-stream")
.Produces<MatchResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status413PayloadTooLarge)
.Produces(StatusCodes.Status415UnsupportedMediaType);

app.Run();

// Exposto para testes de integração (WebApplicationFactory<Program>).
public partial class Program { }
