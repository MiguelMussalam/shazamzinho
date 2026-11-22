using System.Globalization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shazamzin.AudioCatalog;

public sealed class IndexRebuilder(IConnectionMultiplexer connection, ITrackRepository tracks, ILogger<IndexRebuilder> logger)
{
    private const int WriteBatchSize = 5_000;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);

    internal async Task<RedisValue> EnsureReadyAsync(CatalogIndexState state, CancellationToken cancellationToken,
        int? registeredTrack = null, IEnumerable<(uint Hash, int Time)>? hashes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = connection.GetDatabase();
        var marker = await database.StringGetAsync(state.ReadyKey);
        if (PublishedTrackId(marker) >= state.LatestTrackId)
            return marker;

        var owner = Guid.NewGuid().ToString("N");
        if (!await database.LockTakeAsync(state.LockKey, owner, LeaseDuration))
            return RedisValue.Null;

        try
        {
            marker = await database.StringGetAsync(state.ReadyKey);
            var indexedTrack = PublishedTrackId(marker);
            if (indexedTrack >= state.LatestTrackId)
                return marker;

            if (registeredTrack == state.LatestTrackId && indexedTrack == state.LatestTrackId - 1 && hashes is not null)
            {
                foreach (var batch in hashes.Select(hash => (hash.Hash, state.LatestTrackId, hash.Time)).Chunk(WriteBatchSize))
                    if (!await WriteBatchAsync(database, state, owner, batch, cancellationToken))
                        return RedisValue.Null;
            }
            else
            {
                logger.LogInformation("Rebuilding the Valkey fingerprint index through track {LatestTrackId}", state.LatestTrackId);
                if (!await RebuildAsync(database, state, owner, cancellationToken))
                    return RedisValue.Null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            RedisValue ready = string.Create(CultureInfo.InvariantCulture, $"{state.LatestTrackId}:{owner}");
            // A restart that clears the cached rows also clears the lease.
            var publication = database.CreateTransaction();
            publication.AddCondition(Condition.StringEqual(state.LockKey, owner));
            var write = publication.StringSetAsync(state.ReadyKey, ready);
            return await publication.ExecuteAsync() && await write ? ready : RedisValue.Null;
        }
        finally
        {
            await database.LockReleaseAsync(state.LockKey, owner);
        }
    }

    private async Task<bool> RebuildAsync(IDatabase database, CatalogIndexState state, string owner,
        CancellationToken cancellationToken)
    {
        var pending = new List<(uint Hash, int TrackId, int Time)>(WriteBatchSize);
        await foreach (var row in tracks.StreamAllFingerprintsAsync(cancellationToken))
        {
            // A concurrent registration may have committed after the state was read.
            if (row.TrackId > state.LatestTrackId)
                continue;

            pending.Add(row);
            if (pending.Count < WriteBatchSize)
                continue;

            if (!await WriteBatchAsync(database, state, owner, pending, cancellationToken))
                return false;

            pending.Clear();
        }

        return pending.Count == 0 || await WriteBatchAsync(database, state, owner, pending, cancellationToken);
    }

    private static async Task<bool> WriteBatchAsync(IDatabase database, CatalogIndexState state, string owner,
        IReadOnlyCollection<(uint Hash, int TrackId, int Time)> postings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await database.LockExtendAsync(state.LockKey, owner, LeaseDuration))
            return false;

        var batch = database.CreateBatch();
        var writes = postings.GroupBy(posting => posting.Hash).Select(group =>
            batch.SetAddAsync(state.HashKey(group.Key), group.Select(posting =>
                (RedisValue)string.Create(CultureInfo.InvariantCulture, $"{posting.TrackId}:{posting.Time}")).ToArray()))
            .ToArray();

        batch.Execute();
        await Task.WhenAll(writes);
        return true;
    }

    private static int PublishedTrackId(RedisValue marker)
    {
        var text = marker.ToString().AsSpan();
        var separator = text.IndexOf(':');
        return separator > 0 && int.TryParse(text[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : -1;
    }
}
