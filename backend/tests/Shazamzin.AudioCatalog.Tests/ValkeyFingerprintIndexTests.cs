using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Shazamzin.AudioCatalog.Tests;

public sealed class ValkeyFingerprintIndexTests
{
    [RequiresValkey]
    public async Task RebuildsAnEmptyIndexAndIndexesSubsequentTracks()
    {
        const int writeCount = 5_200;
        var cancellationToken = CancellationToken.None;
        var sqliteFile = Path.GetTempFileName();
        var options = ConfigurationOptions.Parse(Environment.GetEnvironmentVariable("AUDIO_CATALOG_TEST_VALKEY")!);
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();
        var sqlite = new SqliteConnectionFactory(
            new SqliteConnectionStringBuilder { DataSource = sqliteFile, Pooling = false }.ToString());
        var tracks = new SqliteTrackRepository(sqlite);
        await sqlite.InitializeAsync(cancellationToken);
        var catalog = await tracks.GetIndexStateAsync(cancellationToken);
        var prefix = $"shazamzinho:audio:v1:{{{catalog.CatalogId}}}";
        var readyKey = (RedisKey)$"{prefix}:ready";
        var firstKey = (RedisKey)$"{prefix}:fp:00000012";
        var secondKey = (RedisKey)$"{prefix}:fp:ffffffff";

        try
        {
            var rebuilder = new IndexRebuilder(connection, tracks, NullLogger<IndexRebuilder>.Instance);
            var index = new ValkeyFingerprintIndex(connection, tracks, rebuilder, NullLogger<ValkeyFingerprintIndex>.Instance);

            var firstHashes = Enumerable.Range(0, writeCount).Select(time => (Hash: 0x12u, Time: time)).ToArray();
            var firstTrack = await tracks.AddTrackAsync("First", null, 6000, firstHashes, cancellationToken);
            Assert.False(await database.KeyExistsAsync(readyKey));

            var rebuilt = await index.LookupAsync([0x12, uint.MaxValue], cancellationToken);

            Assert.Equal(writeCount, rebuilt[0x12].Length);
            Assert.Contains((firstTrack, writeCount - 1), rebuilt[0x12]);
            Assert.Equal(writeCount, await database.SetLengthAsync(firstKey));

            var secondHashes = new[] { (Hash: uint.MaxValue, Time: 25) };
            var secondTrack = await tracks.AddTrackAsync("Second", null, 3000, secondHashes, cancellationToken);
            await index.IndexAsync(secondTrack, secondHashes, cancellationToken);
            Assert.True(await database.SetContainsAsync(secondKey, $"{secondTrack}:25"));
            Assert.Equal((secondTrack, 25), Assert.Single((await index.LookupAsync([uint.MaxValue], cancellationToken))[uint.MaxValue]));

            // Simulate losing this catalog's cached data without touching other catalogs.
            await database.KeyDeleteAsync([readyKey, firstKey, secondKey]);
            var restored = await index.LookupAsync([0x12, uint.MaxValue], cancellationToken);
            Assert.Equal(writeCount, restored[0x12].Length);
            Assert.Equal((secondTrack, 25), Assert.Single(restored[uint.MaxValue]));
            Assert.Equal(1, await database.SetLengthAsync(secondKey));
        }
        finally
        {
            await database.KeyDeleteAsync([readyKey, firstKey, secondKey]);
            File.Delete(sqliteFile);
        }
    }

    private sealed class RequiresValkeyAttribute : FactAttribute
    {
        public RequiresValkeyAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIO_CATALOG_TEST_VALKEY")))
                Skip = "Set AUDIO_CATALOG_TEST_VALKEY to run the Valkey integration test.";
        }
    }
}
