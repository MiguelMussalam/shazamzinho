using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Ingestion;
using Shazamzin.Infrastructure.Native;
using StackExchange.Redis;

namespace Shazamzin.Api.Ingestion;

internal static class FingerprintIngestionEndpoint
{
    internal const string Path = "/catalog/fingerprints";
    internal const string Name = "AddCatalogFingerprints";
    internal const string KeyHeader = "X-Ingestion-Key";
    private const int MaxRequestBytes = 4 * 1024 * 1024;

    public static void MapFingerprintIngestion(this WebApplication app)
    {
        app.MapPost(Path, AddAsync)
            .WithName(Name)
            .WithSummary("Cadastra fingerprints de referência no catálogo")
            .WithDescription("Requer X-Ingestion-Key. Reenvios da mesma faixa retornam a música existente.")
            .Produces<IngestionResult>(StatusCodes.Status201Created)
            .Produces<IngestionResult>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    public static void LimitBody(HttpContext context)
    {
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = MaxRequestBytes;
    }

    private static async Task<IResult> AddAsync(FingerprintIngestionRequest request,
        [FromServices] CatalogWriter writer, IConfiguration configuration,
        ILogger<CatalogWriter> logger, CancellationToken cancellationToken)
    {
        if (request.FileSha256 is not { Length: 64 } sha256 || !sha256.All(Uri.IsHexDigit) ||
            !ValidText(request.Title, 256) || !ValidText(request.Artist, 256) ||
            request.Album?.Length > 256 || request.DurationSec < 0 ||
            request.Fingerprints is not { Length: > 0 and <= FingerprintCodec.MaxCount } entries ||
            entries.Any(entry => entry?.Hash is null || entry.TimestampMs is not (<= int.MaxValue)))
        {
            return Results.BadRequest(new { error = "Metadados ou fingerprints inválidos." });
        }

        var profile = configuration["Ingestion:Profile"];
        if (string.IsNullOrWhiteSpace(profile))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!string.Equals(profile, request.Profile, StringComparison.Ordinal))
            return Results.Conflict(new { error = "Perfil de fingerprints incompatível." });

        var fingerprints = entries.Select(entry =>
            new Fingerprint(entry!.Hash.GetValueOrDefault(), entry.TimestampMs.GetValueOrDefault())).ToArray();
        sha256 = sha256.ToLowerInvariant();
        var metadata = new AudioMetadata(request.Title!.Trim(), request.Artist!.Trim(),
            request.Album?.Trim(), request.DurationSec);
        try
        {
            var result = await writer.AddAsync("api:" + sha256, sha256, metadata, fingerprints, profile, cancellationToken);
            logger.LogInformation("Ingestão concluída: song={SongId} fingerprints={Count} skipped={Skipped}",
                result.SongId, result.Fingerprints, result.Skipped);
            return result.Skipped ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status201Created);
        }
        catch (InvalidOperationException error)
        {
            logger.LogWarning("Catálogo recusou a ingestão: {Reason}", error.Message);
            return Results.Conflict(new { error = "Catálogo incompatível com o perfil ou índice configurado." });
        }
        catch (Exception error) when (error is RedisException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(error, "A ingestão não pôde ser concluída.");
            return Results.Json(new { error = "Catálogo indisponível. Tente novamente." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool ValidText(string? value, int maxLength) =>
        value is not null && value.Length <= maxLength && !string.IsNullOrWhiteSpace(value);
}
