namespace Shazamzin.Core.Interfaces;

/// <summary>Corpo do GET /health (docs/architecture.md §5.2). valkey/sqlite: "up", "down" ou "disabled".</summary>
public sealed record HealthReport(string Status, string Valkey, string Sqlite, int IndexedSongs)
{
    [System.Text.Json.Serialization.JsonIgnore] // decide o HTTP 200/503; não faz parte do corpo do /health
    public bool IsHealthy => Status == "ok";
}

public interface IHealthProbe
{
    Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default);
}
