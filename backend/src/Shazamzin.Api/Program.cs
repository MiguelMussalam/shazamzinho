// Shazamzin.Api — ASP.NET Core 10 Minimal API
//
// Fase 1: POST /match recebe o payload binário do ESP (docs/protocol.md), decodifica,
// chama IMatchingService (ainda fake) e devolve JSON em snake_case. API key obrigatória
// em /match; /health aberto. Matching real contra Valkey/SQLite entra na Fase 2.

using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Serilog.Context;
using Shazamzin.Api.Middleware;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Interfaces;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Matching;

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
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IMatchingService, FakeMatchingService>(); // Fase 2: ValkeyMatchingService

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi(); // /openapi/v1.json

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

// Health check — nas Fases 2+ passa a checar Valkey e SQLite de verdade
// e a reportar indexed_songs (ver seção 5.2 do architecture.md).
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    valkey = "unknown",   // Fase 2: PING no Valkey
    sqlite = "unknown",   // Fase 2: SELECT no SQLite
    indexed_songs = 0
})).WithName("Health");

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
.Accepts<byte[]>("application/octet-stream")
.Produces<MatchResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status413PayloadTooLarge)
.Produces(StatusCodes.Status415UnsupportedMediaType);

app.Run();

// Exposto para testes de integração (WebApplicationFactory<Program>).
public partial class Program { }
