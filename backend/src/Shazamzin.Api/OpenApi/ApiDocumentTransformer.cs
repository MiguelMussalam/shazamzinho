using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Shazamzin.Api.Middleware;

namespace Shazamzin.Api.OpenApi;

/// <summary>Documenta a autenticação do middleware e o corpo binário lido manualmente pelo /match.</summary>
internal sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info.Title = "Shazamzin API";
        document.Info.Version = "v1";
        document.Info.Description = "Reconhecimento de músicas por fingerprints acústicos. " +
            "Use /health para verificar o catálogo e /match para consultar uma captura do ESP32. " +
            "A documentação interativa está disponível apenas em Development.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyAuthMiddleware.HeaderName,
            In = ParameterLocation.Header,
            Description = "Informe a chave configurada em Auth:ApiKey no servidor."
        };

        if (document.Paths.TryGetValue("/match", out var matchPath) && matchPath.Operations is { } operations)
        {
            foreach (var operation in operations.Values)
            {
                operation.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
                    }
                ];
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-Device-ID",
                    In = ParameterLocation.Header,
                    Required = false,
                    Description = "Identificador do dispositivo para os logs, por exemplo esp32-01.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });

                // byte[] seria descrito como string base64. O endpoint recebe um arquivo binário bruto.
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Description = "Arquivo .bin no protocolo v1: header de 8 bytes + 8 bytes por fingerprint, little-endian.",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/octet-stream"] = new()
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                        }
                    }
                };
            }
        }

        return Task.CompletedTask;
    }
}
