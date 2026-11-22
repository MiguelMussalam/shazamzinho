using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shazamzin.AudioCatalog;

public sealed class ValkeyFingerprintIndex(IConnectionMultiplexer connection, ITrackRepository tracks,
    IndexRebuilder rebuilder, ILogger<ValkeyFingerprintIndex> logger) : IFingerprintIndex
{
    private const int LookupBatchSize = 500;

    public async Task IndexAsync(int trackId, IEnumerable<(uint Hash, int Time)> hashes, CancellationToken cancellationToken)
    {
        try
        {
            var state = await tracks.GetIndexStateAsync(cancellationToken);
            if ((await rebuilder.EnsureReadyAsync(state, cancellationToken, trackId, hashes)).IsNull)
                logger.LogWarning("Track {TrackId} was saved in SQLite. The Valkey index will be refreshed on demand", trackId);
        }
        catch (Exception error) when (error is RedisException or SqliteException)
        {
            logger.LogWarning("Track {TrackId} was saved in SQLite. Valkey indexing was deferred ({ErrorType}: {Detail})",
                trackId, error.GetType().Name, error is RedisServerException ? error.Message : "Unavailable");
        }
    }

    public async Task<IReadOnlyDictionary<uint, (int TrackId, int Time)[]>> LookupAsync(
        IReadOnlyList<uint> hashes, CancellationToken cancellationToken)
    {
        if (hashes.Count == 0)
            return new Dictionary<uint, (int TrackId, int Time)[]>();

        try
        {
            var state = await tracks.GetIndexStateAsync(cancellationToken);
            var marker = await rebuilder.EnsureReadyAsync(state, cancellationToken);
            if (!marker.IsNull)
            {
                var database = connection.GetDatabase();
                var hits = await LookupReadyIndexAsync(database, state, hashes, cancellationToken);

                // A reset or publication during lookup can make a pipelined read incomplete.
                if (await database.StringGetAsync(state.ReadyKey) == marker)
                    return hits;
            }

            logger.LogWarning("The Valkey index changed or is rebuilding. Looking up fingerprints in SQLite");
        }
        catch (Exception error) when (error is RedisException or FormatException)
        {
            logger.LogWarning("The Valkey index is unavailable ({ErrorType}: {Detail}). Looking up fingerprints in SQLite",
                error.GetType().Name, error is RedisServerException ? error.Message : "Unavailable");
        }

        return await tracks.FindByHashesAsync(hashes, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<uint, (int TrackId, int Time)[]>> LookupReadyIndexAsync(
        IDatabase database, CatalogIndexState state, IReadOnlyList<uint> hashes, CancellationToken cancellationToken)
    {
        var hits = new Dictionary<uint, (int TrackId, int Time)[]>();
        foreach (var hashesInBatch in hashes.Distinct().Chunk(LookupBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = database.CreateBatch();
            var reads = hashesInBatch.Select(hash => batch.SetMembersAsync(state.HashKey(hash))).ToArray();
            batch.Execute();
            var responses = await Task.WhenAll(reads);

            for (var i = 0; i < responses.Length; i++)
            {
                var postings = new List<(int TrackId, int Time)>();
                foreach (var value in responses[i])
                {
                    var posting = ParsePosting(value);
                    if (posting.TrackId <= state.LatestTrackId)
                        postings.Add(posting);
                }

                if (postings.Count > 0)
                    hits.Add(hashesInBatch[i], postings.ToArray());
            }
        }

        return hits;
    }

    private static (int TrackId, int Time) ParsePosting(RedisValue value)
    {
        var text = value.ToString().AsSpan();
        var separator = text.IndexOf(':');
        if (separator > 0 &&
            int.TryParse(text[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var track) && track > 0 &&
            int.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var time))
            return (track, time);

        throw new FormatException("An invalid fingerprint posting was found.");
    }
}
