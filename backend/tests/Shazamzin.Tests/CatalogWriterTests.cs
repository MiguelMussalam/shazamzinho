using Microsoft.Data.Sqlite;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Ingestion;
using Shazamzin.Infrastructure.Native;
using Shazamzin.Infrastructure.Sqlite;
using Shazamzin.Infrastructure.Valkey;
using StackExchange.Redis;

namespace Shazamzin.Tests;

public sealed class IngestionFactAttribute : FactAttribute
{
    public IngestionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHAZAMZIN_TEST_VALKEY")))
            Skip = "Defina SHAZAMZIN_TEST_VALKEY apontando para um banco Valkey descartável, diferente de 0.";
    }
}

/// <summary>Requer um índice descartável explícito; nunca usa o catálogo padrão nem o DB 15 da suíte antiga.</summary>
public sealed class CatalogWriterTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shazamzin-ingestion-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";
    private ConnectionMultiplexer? _redis;
    private CatalogWriter Writer => new(ConnectionString, _redis!);
    private readonly AudioMetadata _metadata = new("Música de teste", "Artista", null, 10);
    private readonly Fingerprint[] _fingerprints = [new(100, 0), new(100, 16), new(200, 32)];

    public async Task InitializeAsync()
    {
        var target = Environment.GetEnvironmentVariable("SHAZAMZIN_TEST_VALKEY");
        if (string.IsNullOrWhiteSpace(target)) return;
        var options = ConfigurationOptions.Parse(target);
        if (options.DefaultDatabase is null or 0 or 15)
            throw new InvalidOperationException("O índice descartável deve indicar defaultDatabase diferente de 0 e 15.");
        options.AllowAdmin = true;
        _redis = await ConnectionMultiplexer.ConnectAsync(options);
        await _redis.GetServer(_redis.GetEndPoints()[0]).FlushDatabaseAsync(options.DefaultDatabase.Value);
    }

    [IngestionFact]
    public async Task RepeatingAFile_DoesNotDuplicateSongsOrPostings()
    {
        var first = await Writer.AddAsync("x.mp3", "file-a", _metadata, _fingerprints, "test-profile");
        var second = await Writer.AddAsync("x.mp3", "file-a", _metadata, _fingerprints, "test-profile");
        Assert.False(first.Skipped);
        Assert.True(second.Skipped);
        Assert.Equal(first.SongId, second.SongId);
        var blob = (byte[])(await _redis!.GetDatabase().StringGetAsync(PostingsCodec.KeyFor(100)))!;
        Assert.Equal(2, PostingsCodec.Decode(blob).Length);
        Assert.Equal(1, await new SqliteSongRepository(ConnectionString).CountAsync());
    }

    [IngestionFact]
    public async Task PartialRedisFailure_HidesTheSong_AndRetryRepairsWithoutReusingIdsOrDuplicatingPostings()
    {
        var db = _redis!.GetDatabase();
        await Writer.AddAsync("baseline.mp3", "baseline", _metadata, [new(900, 0)], "test-profile");
        await db.ListRightPushAsync(PostingsCodec.KeyFor(200), "wrong-type-for-fault-injection");
        await Assert.ThrowsAsync<RedisServerException>(() => Writer.AddAsync("x.mp3", "file-a", _metadata, _fingerprints, "test-profile"));
        var songs = new SqliteSongRepository(ConnectionString);
        Assert.Null(await songs.GetByIdAsync(2));
        Assert.Equal(1, await songs.CountAsync());
        var other = await Writer.AddAsync("other.mp3", "file-b", _metadata, [new(300, 0)], "test-profile");
        Assert.Equal(3, other.SongId);
        await db.KeyDeleteAsync(PostingsCodec.KeyFor(200));
        var repaired = await Writer.AddAsync("x.mp3", "file-a", _metadata, _fingerprints, "test-profile");
        Assert.Equal(2, repaired.SongId);
        Assert.Equal(3, await songs.CountAsync());
        var blob = (byte[])(await db.StringGetAsync(PostingsCodec.KeyFor(100)))!;
        Assert.Equal(2, PostingsCodec.Decode(blob).Length);
        Assert.All(PostingsCodec.Decode(blob), p => Assert.Equal(2, p.SongId));
    }

    [IngestionFact]
    public async Task DifferentProfile_IsRejected_AndExistingCatalogIsPreserved()
    {
        await Writer.AddAsync("x.mp3", "file-a", _metadata, _fingerprints, "test-profile");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Writer.AddAsync("other.mp3", "file-b", _metadata, _fingerprints, "incompatible-profile"));
        Assert.Equal(1, await new SqliteSongRepository(ConnectionString).CountAsync());
    }

    [IngestionFact]
    public async Task LegacyCatalogWithoutProfile_IsRejectedWithoutChangingItsSongsOrValkey()
    {
        SqliteInitializer.EnsureCreated(ConnectionString);
        await using (var sql = new SqliteConnection(ConnectionString))
        {
            await sql.OpenAsync();
            using var command = sql.CreateCommand();
            command.CommandText = "INSERT INTO songs(title,artist,file_path,file_hash) VALUES ('Legado','Artista','legacy.mp3','legacy')";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Writer.AddAsync("x.mp3", "new-file", _metadata, _fingerprints, "test-profile"));
        Assert.Equal(1, await new SqliteSongRepository(ConnectionString).CountAsync());
        Assert.False(await _redis!.GetDatabase().KeyExistsAsync("shazamzin:catalog"));
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(PostingsCodec.KeyFor(100)));
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.GetServer(_redis.GetEndPoints()[0]).FlushDatabaseAsync(_redis.GetDatabase().Database);
            await _redis.DisposeAsync();
        }
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", ".ingestion.lock" })
            if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix);
    }
}
