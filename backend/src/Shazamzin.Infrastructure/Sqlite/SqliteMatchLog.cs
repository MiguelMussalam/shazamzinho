using Dapper;
using Microsoft.Data.Sqlite;
using Shazamzin.Core.Interfaces;
using Shazamzin.Core.Models;

namespace Shazamzin.Infrastructure.Sqlite;

/// <summary>Grava cada consulta ao /match em match_log (dispositivo, música, confiança, hashes, tempo).</summary>
public sealed class SqliteMatchLog : IMatchLog
{
    private readonly string _connectionString;

    public SqliteMatchLog(string connectionString) => _connectionString = connectionString;

    public async Task RecordAsync(string deviceId, int hashesReceived, MatchResult result, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO match_log (device_id, matched_song_id, confidence, hashes_received, hashes_hit, processing_time_ms) " +
            "VALUES (@deviceId, @songId, @confidence, @hashesReceived, @hashesHit, @processingTimeMs)",
            new
            {
                deviceId,
                songId = result.Match ? result.SongId : null,
                confidence = result.Match ? result.Confidence : null,
                hashesReceived,
                hashesHit = result.HashesHit,
                processingTimeMs = result.ProcessingTimeMs,
            },
            cancellationToken: cancellationToken));
    }
}

/// <summary>Com o matching fake não há banco: nada a gravar.</summary>
public sealed class NoMatchLog : IMatchLog
{
    public Task RecordAsync(string deviceId, int hashesReceived, MatchResult result, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
