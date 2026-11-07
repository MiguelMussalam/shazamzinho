// Shazamzin.Api — ASP.NET Core 10 Minimal API
//
// Fase 0: apenas o health check (GET /health) para validar que o servidor sobe.
// O endpoint POST /match, o middleware de API key e o Serilog completo entram
// na Fase 1 (ver docs/architecture.md, seção 8).

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Health check — por enquanto reporta apenas que o processo está de pé.
// Nas Fases 2+ este endpoint passa a checar Valkey e SQLite de verdade
// e a reportar indexed_songs (ver seção 5.2 do architecture.md).
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    valkey = "unknown",   // Fase 2: PING no Valkey
    sqlite = "unknown",   // Fase 2: SELECT no SQLite
    indexed_songs = 0
}));

app.Run();

// Exposto para testes de integração (WebApplicationFactory<Program>).
public partial class Program { }
