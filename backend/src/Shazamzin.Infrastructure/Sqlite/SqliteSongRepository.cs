using Dapper;
using Microsoft.Data.Sqlite;
using Shazamzin.Core.Interfaces;
using Shazamzin.Core.Models;

namespace Shazamzin.Infrastructure.Sqlite;

/// <summary>Metadados das músicas no SQLite (tabela songs, docs/architecture.md §5.4).</summary>
public sealed class SqliteSongRepository : ISongRepository
{
    private readonly string _connectionString;

    public SqliteSongRepository(string connectionString) => _connectionString = connectionString;

    private sealed class SongRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string? Album { get; set; }
        public long? DurationSec { get; set; }
    }

    public async Task<Song?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var row = await connection.QuerySingleOrDefaultAsync<SongRow>(new CommandDefinition(
            "SELECT id AS Id, title AS Title, artist AS Artist, album AS Album, duration_sec AS DurationSec " +
            "FROM songs WHERE id = @id",
            new { id }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new Song((int)row.Id, row.Title, row.Artist, row.Album, (int?)row.DurationSec);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM songs", cancellationToken: cancellationToken));
    }
}
