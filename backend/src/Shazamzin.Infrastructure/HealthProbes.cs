using Shazamzin.Core.Interfaces;
using StackExchange.Redis;

namespace Shazamzin.Infrastructure;

/// <summary>PING no Valkey e COUNT no SQLite. Cada dependência é testada separada: uma fora do ar não esconde a outra.</summary>
public sealed class ValkeyHealthProbe : IHealthProbe
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ISongRepository _songs;

    public ValkeyHealthProbe(IConnectionMultiplexer redis, ISongRepository songs)
    {
        _redis = redis;
        _songs = songs;
    }

    public async Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var valkey = "up";
        try { await _redis.GetDatabase().PingAsync(); }
        catch (Exception) { valkey = "down"; }

        var sqlite = "up";
        var songs = 0;
        try { songs = await _songs.CountAsync(cancellationToken); }
        catch (Exception) { sqlite = "down"; }

        var status = valkey == "up" && sqlite == "up" ? "ok" : "degraded";
        return new HealthReport(status, valkey, sqlite, songs);
    }
}

/// <summary>Usado com o matching fake (testes e desenvolvimento sem infraestrutura): nada a checar.</summary>
public sealed class DisabledHealthProbe : IHealthProbe
{
    public Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HealthReport("ok", "disabled", "disabled", 0));
}
