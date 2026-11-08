using FluentAssertions;
using Shazamzin.Core.Interfaces;
using Shazamzin.Core.Matching;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Valkey;
using StackExchange.Redis;
using Xunit;

namespace Shazamzin.Tests;

/// <summary>Pula o teste (em vez de falhar) quando não há Valkey em localhost:6379.</summary>
public sealed class ValkeyFactAttribute : FactAttribute
{
    public static readonly string? UnavailableReason = Probe();

    public ValkeyFactAttribute()
    {
        if (UnavailableReason is not null) Skip = UnavailableReason;
    }

    private static string? Probe()
    {
        try
        {
            using var mux = ConnectionMultiplexer.Connect(ValkeyMatchingServiceTests.Options);
            mux.GetDatabase().Ping();
            return null;
        }
        catch (Exception e)
        {
            return $"Valkey indisponivel em localhost:6379 ({e.GetType().Name}). Rode scripts/setup-valkey.ps1.";
        }
    }
}

/// <summary>
/// Integração com um Valkey real. Usa o database 15 (o catálogo de verdade fica no 0),
/// que é esvaziado antes de cada teste.
/// </summary>
public sealed class ValkeyMatchingServiceTests : IAsyncLifetime
{
    internal static readonly ConfigurationOptions Options =
        ConfigurationOptions.Parse("localhost:6379,defaultDatabase=15,allowAdmin=true,connectTimeout=1500,abortConnect=true");

    private sealed class FakeSongs : ISongRepository
    {
        public Dictionary<int, Song> Songs { get; } = new();
        public Task<Song?> GetByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Songs.GetValueOrDefault(id));
        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(Songs.Count);
    }

    private readonly FakeSongs _songs = new();
    private ConnectionMultiplexer? _mux;

    public async Task InitializeAsync()
    {
        if (ValkeyFactAttribute.UnavailableReason is not null) return;
        _mux = await ConnectionMultiplexer.ConnectAsync(Options);
        await _mux.GetServer(_mux.GetEndPoints()[0]).FlushDatabaseAsync(15);
    }

    public async Task DisposeAsync()
    {
        if (_mux is null) return;
        await _mux.GetServer(_mux.GetEndPoints()[0]).FlushDatabaseAsync(15);
        await _mux.DisposeAsync();
    }

    private ValkeyMatchingService Service() => new(_mux!, _songs, new MatchingOptions());

    /// <summary>Indexa uma música sintética com o mesmo APPEND que a ingestão vai usar.</summary>
    private async Task IndexSongAsync(int songId, uint firstHash, int hashes, int stepMs)
    {
        var db = _mux!.GetDatabase();
        for (var i = 0; i < hashes; i++)
            await db.StringAppendAsync(PostingsCodec.KeyFor(firstHash + (uint)i), PostingsCodec.EncodeOne(new Posting(songId, i * stepMs)));
    }

    [ValkeyFact]
    public async Task IdentifiesSong_FromASliceOfItsFingerprints()
    {
        _songs.Songs[2] = new Song(2, "Redemption Song", "Bob Marley", "Uprising", 227);
        await IndexSongAsync(1, firstHash: 1_000_000, hashes: 300, stepMs: 100);
        await IndexSongAsync(2, firstHash: 2_000_000, hashes: 300, stepMs: 100);

        // captura de 60 hashes que começa em 5,0 s dentro da música 2 (hash i está em i*100 ms)
        var slice = Enumerable.Range(50, 60)
            .Select(i => new Fingerprint(2_000_000 + (uint)i, (uint)((i - 50) * 100)))
            .ToArray();

        var result = await Service().MatchAsync(new FingerprintPayload(1, slice));

        result.Match.Should().BeTrue();
        result.SongId.Should().Be(2);
        result.Title.Should().Be("Redemption Song");
        result.Artist.Should().Be("Bob Marley");
        result.Confidence.Should().Be(1.0);
        result.MatchedOffsetSec.Should().Be(5.0);
    }

    [ValkeyFact]
    public async Task ReturnsNoMatch_WithHitCounts_WhenNothingAligns()
    {
        await IndexSongAsync(1, firstHash: 1_000_000, hashes: 300, stepMs: 100);

        // hashes que existem no índice, mas com timestamps que não formam alinhamento
        var scattered = Enumerable.Range(0, 20)
            .Select(i => new Fingerprint(1_000_000 + (uint)(i * 13), (uint)(i * 777)))
            .ToArray();
        var unknown = new[] { new Fingerprint(0xDEADBEEF, 0) };

        var result = await Service().MatchAsync(new FingerprintPayload(1, scattered.Concat(unknown).ToArray()));

        result.Match.Should().BeFalse();
        result.HashesReceived.Should().Be(21);
        result.HashesHit.Should().Be(20);
        result.Title.Should().BeNull();
    }

    [ValkeyFact]
    public async Task TreatsIndexedSongMissingFromSqlite_AsNoMatch()
    {
        // índice conhece a música 5, mas o SQLite não: dessincronia => no-match, sem estourar
        await IndexSongAsync(5, firstHash: 3_000_000, hashes: 40, stepMs: 100);
        var query = Enumerable.Range(0, 40)
            .Select(i => new Fingerprint(3_000_000 + (uint)i, (uint)(i * 100)))
            .ToArray();

        var result = await Service().MatchAsync(new FingerprintPayload(1, query));

        result.Match.Should().BeFalse();
    }

    [ValkeyFact]
    public async Task EmptyQuery_IsNoMatch()
    {
        var result = await Service().MatchAsync(new FingerprintPayload(1, Array.Empty<Fingerprint>()));

        result.Match.Should().BeFalse();
        result.HashesReceived.Should().Be(0);
        result.HashesHit.Should().Be(0);
    }
}
