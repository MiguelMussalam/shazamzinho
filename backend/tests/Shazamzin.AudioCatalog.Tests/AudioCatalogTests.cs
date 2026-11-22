using Microsoft.Data.Sqlite;
using Shazamzin.AudioFingerprinting;
using Xunit;

namespace Shazamzin.AudioCatalog.Tests;

public sealed class AudioCatalogTests : IDisposable
{
    private const int SampleRate = FingerprintOptions.TargetSampleRate;
    private const int Hop = FingerprintOptions.Hop;
    private static readonly CancellationToken NoCancellation = CancellationToken.None;
    private readonly string _databasePath = Path.GetTempFileName();
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteTrackRepository _tracks;

    public AudioCatalogTests()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false };
        _connections = new SqliteConnectionFactory(connectionString.ToString());
        _tracks = new SqliteTrackRepository(_connections);
    }

    [Fact]
    public async Task IdentifiesNoisyExcerptAndOffsetAfterReopeningCatalog()
    {
        await _connections.InitializeAsync(NoCancellation);
        var firstSession = CreateService();
        var recording = CreateRecording(2);
        await firstSession.RegisterTrackAsync(CreateWave(CreateRecording(1)), "First Track", null, NoCancellation);
        await firstSession.RegisterTrackAsync(CreateWave(recording), "Target Track", "Target Artist", NoCancellation);
        await firstSession.RegisterTrackAsync(CreateWave(CreateRecording(3)), "Third Track", null, NoCancellation);

        const int start = 4 * SampleRate + 177;
        var excerpt = recording.AsSpan(start, 5 * SampleRate).ToArray();
        var noise = new Random(42);
        for (var i = 0; i < excerpt.Length; i++)
            excerpt[i] += (float)((noise.NextDouble() - 0.5) * 0.02);

        await _connections.InitializeAsync(NoCancellation);
        var secondSession = CreateService();
        var match = await secondSession.IdentifyAsync(CreateWave(excerpt), NoCancellation);

        Assert.NotNull(match);
        Assert.Equal("Target Track", match.Title);
        Assert.Equal("Target Artist", match.Artist);
        Assert.InRange(match.OffsetSeconds, (double)start / SampleRate - 0.1, (double)start / SampleRate + 0.1);
        Assert.True(match.Score >= 20);
        Assert.Null(await secondSession.IdentifyAsync(CreateWave(new float[5 * SampleRate]), NoCancellation));
        Assert.Null(await CreateService(minimumScore: int.MaxValue).IdentifyAsync(CreateWave(excerpt), NoCancellation));
    }

    [Fact]
    public async Task RollsBackTrackAndFingerprintsIfRegistrationFailsPartway()
    {
        await _connections.InitializeAsync(NoCancellation);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _tracks.AddTrackAsync("Incomplete", null, 1000, InterruptedHashes(), NoCancellation));

        await using var connection = await _connections.OpenAsync(NoCancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM tracks) + (SELECT COUNT(*) FROM fingerprints);";
        Assert.Equal(0L, await command.ExecuteScalarAsync(NoCancellation));

        static IEnumerable<(uint Hash, int Time)> InterruptedHashes()
        {
            yield return (1, 10);
            throw new InvalidOperationException("Simulated registration failure.");
        }
    }

    [Fact]
    public async Task ReadsUnsignedHashesAcrossLookupBatchesAndStreamsTheIndex()
    {
        await _connections.InitializeAsync(NoCancellation);
        var trackId = await _tracks.AddTrackAsync("Stored Track", null, 4000,
            [(0, 10), (uint.MaxValue, 3000)], NoCancellation);
        var hashes = Enumerable.Range(0, 1_001).Select(value => (uint)value).Append(uint.MaxValue).ToArray();
        var matches = await _tracks.FindByHashesAsync(hashes, NoCancellation);

        Assert.Equal(2, matches.Count);
        Assert.Equal((trackId, 10), Assert.Single(matches[0]));
        Assert.Equal((trackId, 3000), Assert.Single(matches[uint.MaxValue]));
        Assert.Equal((trackId, 3000), Assert.Single(await _tracks.FindByHashAsync(uint.MaxValue, NoCancellation)));
        Assert.Empty(await _tracks.FindByHashAsync(5, NoCancellation));
        Assert.Equal(("Stored Track", (string?)null), await _tracks.GetTrackAsync(trackId, NoCancellation));

        var stored = new List<(uint Hash, int TrackId, int Time)>();
        await foreach (var value in _tracks.StreamAllFingerprintsAsync(NoCancellation))
            stored.Add(value);

        Assert.Equal(new[] { (0u, trackId, 10), (uint.MaxValue, trackId, 3000) },
            stored.OrderBy(value => value.Hash));
    }

    [Fact]
    public async Task InitializesWalAndEnforcesForeignKeysOnNewConnections()
    {
        await _connections.InitializeAsync(NoCancellation);
        await using var connection = await _connections.OpenAsync(NoCancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", await command.ExecuteScalarAsync(NoCancellation));

        command.CommandText = "INSERT INTO fingerprints (hash, track_id, t_offset) VALUES (1, 999, 0);";
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(NoCancellation));
    }

    [Fact]
    public async Task DoesNotAddTablesToAnOlafCatalog()
    {
        await using var connection = await _connections.OpenAsync(NoCancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE catalog_settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync(NoCancellation);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _connections.InitializeAsync(NoCancellation));

        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name IN ('tracks', 'fingerprints');";
        Assert.Equal(0L, await command.ExecuteScalarAsync(NoCancellation));
    }

    private FingerprintService CreateService(int minimumScore = 20) =>
        new(new AudioInputNormalizer(), new AudioFingerprinter(), _tracks, new IdentificationOptions { MinScore = minimumScore });

    private static float[] CreateRecording(int seed)
    {
        int[] bands = [8, 27, 52, 101, 200, 396, 780];
        var random = new Random(seed);
        var notes = Enumerable.Range(0, 28)
            .Select(_ => bands.Select((band, index) => band + random.Next(index + 8)).ToArray())
            .ToArray();

        const int noteLength = 11 * Hop;
        var samples = new float[12 * SampleRate];
        for (var i = 0; i < samples.Length; i++)
        {
            var note = i / noteLength;
            var current = notes[note];
            var previous = notes[Math.Max(0, note - 1)];
            var mix = Math.Min(1.0, (double)(i % noteLength) / Hop);
            var angle = 2 * Math.PI * i / FingerprintOptions.WindowSize;

            double sum = 0;
            for (var band = 0; band < bands.Length; band++)
            {
                sum += Math.Sin(angle * current[band]) * mix +
                    Math.Sin(angle * previous[band]) * (1 - mix);
            }

            samples[i] = (float)(sum * 0.1);
        }

        return samples;
    }

    private static byte[] CreateWave(float[] samples)
    {
        var dataSize = samples.Length * sizeof(short);
        using var bytes = new MemoryStream();
        using var wave = new BinaryWriter(bytes);
        wave.Write("RIFF"u8);
        wave.Write(36 + dataSize);
        wave.Write("WAVEfmt "u8);
        wave.Write(16);
        wave.Write((ushort)1);
        wave.Write((ushort)1);
        wave.Write(SampleRate);
        wave.Write(SampleRate * sizeof(short));
        wave.Write((ushort)sizeof(short));
        wave.Write((ushort)(8 * sizeof(short)));
        wave.Write("data"u8);
        wave.Write(dataSize);
        foreach (var sample in samples)
            wave.Write((short)Math.Round(sample * short.MaxValue));

        return bytes.ToArray();
    }

    public void Dispose() => File.Delete(_databasePath);
}
