using System.Security.Cryptography;
using System.Text;
using Shazamzin.Api.Ingestion;

namespace Shazamzin.Api.Middleware;

/// <summary>
/// Exige o header X-Api-Key (valor em Auth:ApiKey) nas rotas protegidas.
/// O cadastro do catálogo usa uma chave própria.
/// Comparação em tempo constante para não vazar o tamanho do prefixo correto.
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly byte[] _expectedKey;
    private readonly byte[]? _ingestionKey;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var key = configuration["Auth:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Auth:ApiKey não configurada em appsettings.");
        _expectedKey = Encoding.UTF8.GetBytes(key);
        var ingestionKey = configuration["Ingestion:ApiKey"];
        if (!string.IsNullOrWhiteSpace(ingestionKey))
        {
            if (string.Equals(key, ingestionKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Ingestion:ApiKey deve ser diferente de Auth:ApiKey.");
            _ingestionKey = Encoding.UTF8.GetBytes(ingestionKey);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ingestion = context.GetEndpoint()?.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName
            == FingerprintIngestionEndpoint.Name;
        if (ingestion && _ingestionKey is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (ingestion || context.Request.Path.StartsWithSegments("/match"))
        {
            var header = ingestion ? FingerprintIngestionEndpoint.KeyHeader : HeaderName;
            var expected = ingestion ? _ingestionKey! : _expectedKey;
            var provided = context.Request.Headers[header].ToString();
            var providedBytes = Encoding.UTF8.GetBytes(provided);

            if (providedBytes.Length != expected.Length ||
                !CryptographicOperations.FixedTimeEquals(providedBytes, expected))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "API key ausente ou inválida." });
                return;
            }
        }

        if (ingestion) FingerprintIngestionEndpoint.LimitBody(context);

        await _next(context);
    }
}
