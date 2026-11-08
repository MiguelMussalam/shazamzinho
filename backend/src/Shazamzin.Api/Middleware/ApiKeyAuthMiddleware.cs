using System.Security.Cryptography;
using System.Text;

namespace Shazamzin.Api.Middleware;

/// <summary>
/// Exige o header X-Api-Key (valor em Auth:ApiKey) nas rotas protegidas.
/// Só /match é protegido; /health, /openapi e demais ficam abertos.
/// Comparação em tempo constante para não vazar o tamanho do prefixo correto.
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly byte[] _expectedKey;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var key = configuration["Auth:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Auth:ApiKey não configurada em appsettings.");
        _expectedKey = Encoding.UTF8.GetBytes(key);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/match"))
        {
            var provided = context.Request.Headers[HeaderName].ToString();
            var providedBytes = Encoding.UTF8.GetBytes(provided);

            if (providedBytes.Length != _expectedKey.Length ||
                !CryptographicOperations.FixedTimeEquals(providedBytes, _expectedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "API key ausente ou inválida." });
                return;
            }
        }

        await _next(context);
    }
}
