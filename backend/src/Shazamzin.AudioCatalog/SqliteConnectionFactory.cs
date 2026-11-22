using Microsoft.Data.Sqlite;

namespace Shazamzin.AudioCatalog;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        var options = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = true };
        if (string.IsNullOrWhiteSpace(options.DataSource) || options.Mode == SqliteOpenMode.Memory ||
            options.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The audio catalog requires a persistent SQLite database.", nameof(connectionString));
        }

        _connectionString = options.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'catalog_settings';";
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
            throw new InvalidOperationException("This database belongs to the Olaf catalog. Choose a separate SQLite file.");

        command.CommandText = "PRAGMA journal_mode = WAL;";
        await command.ExecuteScalarAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS audio_catalog_identity (
                singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                catalog_id TEXT NOT NULL
            );

            INSERT OR IGNORE INTO audio_catalog_identity (singleton, catalog_id)
            VALUES (1, lower(hex(randomblob(16))));

            CREATE TABLE IF NOT EXISTS tracks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                artist TEXT,
                duration_ms INTEGER,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS fingerprints (
                hash INTEGER NOT NULL,
                track_id INTEGER NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
                t_offset INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_fp_hash ON fingerprints(hash);
            CREATE INDEX IF NOT EXISTS idx_fp_track ON fingerprints(track_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
