using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Shazamzin.AudioCatalog;

public sealed class SqliteTrackRepository(SqliteConnectionFactory connections) : ITrackRepository
{
    private const int HashBatchSize = 500;

    public async Task<CatalogIndexState> GetIndexStateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT catalog_id, COALESCE((SELECT MAX(id) FROM tracks), 0)
            FROM audio_catalog_identity WHERE singleton = 1;
            """;
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        if (!await row.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The audio catalog has not been initialized.");

        return new CatalogIndexState(row.GetString(0), row.GetInt32(1));
    }

    public async Task<int> AddTrackAsync(string title, string? artist, int durationMs,
        IEnumerable<(uint Hash, int Time)> hashes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(hashes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationMs);

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var addTrack = connection.CreateCommand();
        addTrack.Transaction = transaction;
        addTrack.CommandText = """
            INSERT INTO tracks (title, artist, duration_ms)
            VALUES ($title, $artist, $duration)
            RETURNING id;
            """;
        addTrack.Parameters.AddWithValue("$title", title);
        addTrack.Parameters.AddWithValue("$artist", (object?)artist ?? DBNull.Value);
        addTrack.Parameters.AddWithValue("$duration", durationMs);
        var trackId = checked((int)(long)(await addTrack.ExecuteScalarAsync(cancellationToken))!);

        await using var addHash = connection.CreateCommand();
        addHash.Transaction = transaction;
        addHash.CommandText = "INSERT INTO fingerprints (hash, track_id, t_offset) VALUES ($hash, $track, $offset);";
        var hashParameter = addHash.Parameters.Add("$hash", SqliteType.Integer);
        addHash.Parameters.AddWithValue("$track", trackId);
        var offsetParameter = addHash.Parameters.Add("$offset", SqliteType.Integer);
        addHash.Prepare();

        foreach (var (hash, time) in hashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(time);
            hashParameter.Value = (long)hash;
            offsetParameter.Value = time;
            await addHash.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return trackId;
    }

    public async Task<(string Title, string? Artist)?> GetTrackAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT title, artist FROM tracks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        return await row.ReadAsync(cancellationToken)
            ? (row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1))
            : null;
    }

    public async IAsyncEnumerable<(uint Hash, int TrackId, int Time)> StreamAllFingerprintsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT hash, track_id, t_offset FROM fingerprints;";
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);

        while (await rows.ReadAsync(cancellationToken))
            yield return (checked((uint)rows.GetInt64(0)), rows.GetInt32(1), rows.GetInt32(2));
    }

    public async Task<IReadOnlyList<(int TrackId, int Time)>> FindByHashAsync(
        uint hash, CancellationToken cancellationToken)
    {
        var matches = await FindByHashesAsync([hash], cancellationToken);
        return matches.TryGetValue(hash, out var postings) ? postings : [];
    }

    public async Task<IReadOnlyDictionary<uint, (int TrackId, int Time)[]>> FindByHashesAsync(
        IReadOnlyList<uint> hashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        var matches = new Dictionary<uint, List<(int TrackId, int Time)>>();
        if (hashes.Count == 0)
            return new Dictionary<uint, (int TrackId, int Time)[]>();

        await using var connection = await connections.OpenAsync(cancellationToken);
        // Keep the same snapshot if a registration commits between two batches.
        await using var transaction = connection.BeginTransaction(deferred: true);
        foreach (var batch in hashes.Distinct().Chunk(HashBatchSize))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var parameters = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                parameters[i] = $"$hash{i}";
                command.Parameters.AddWithValue(parameters[i], (long)batch[i]);
            }

            command.CommandText = $"SELECT hash, track_id, t_offset FROM fingerprints WHERE hash IN ({string.Join(", ", parameters)});";
            await using var rows = await command.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
            {
                var hash = checked((uint)rows.GetInt64(0));
                if (!matches.TryGetValue(hash, out var postings))
                    matches.Add(hash, postings = []);

                postings.Add((rows.GetInt32(1), rows.GetInt32(2)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return matches.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}
