using FluentAssertions;
using Microsoft.Data.Sqlite;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Sqlite;
using Xunit;

namespace Shazamzin.Tests;

/// <summary>Usa um arquivo SQLite temporário com o schema.sql real do projeto.</summary>
public sealed class SqliteSongRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shazamzin-test-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly SqliteSongRepository _repository;

    public SqliteSongRepositoryTests()
    {
        _connectionString = $"Data Source={_dbPath}";
        SqliteInitializer.EnsureCreated(_connectionString);
        _repository = new SqliteSongRepository(_connectionString);
    }

    private void Insert(string title, string artist, string? album, int? duration, string hash)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO songs (title, artist, album, duration_sec, file_path, file_hash) " +
            "VALUES ($t, $a, $al, $d, 'x.mp3', $h)";
        command.Parameters.AddWithValue("$t", title);
        command.Parameters.AddWithValue("$a", artist);
        command.Parameters.AddWithValue("$al", (object?)album ?? DBNull.Value);
        command.Parameters.AddWithValue("$d", (object?)duration ?? DBNull.Value);
        command.Parameters.AddWithValue("$h", hash);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task GetById_ReturnsSongWithAllFields()
    {
        Insert("Redemption Song", "Bob Marley", "Uprising", 227, "h1");

        var song = await _repository.GetByIdAsync(1);

        song.Should().Be(new Song(1, "Redemption Song", "Bob Marley", "Uprising", 227));
    }

    [Fact]
    public async Task GetById_MapsNullAlbumAndDuration()
    {
        Insert("Sem Album", "Fulano", null, null, "h2");

        var song = await _repository.GetByIdAsync(1);

        song!.Album.Should().BeNull();
        song.DurationSec.Should().BeNull();
    }

    [Fact]
    public async Task GetById_ReturnsNullWhenMissing() =>
        (await _repository.GetByIdAsync(999)).Should().BeNull();

    [Fact]
    public async Task Count_ReflectsInsertedSongs()
    {
        (await _repository.CountAsync()).Should().Be(0);

        Insert("A", "X", null, null, "ha");
        Insert("B", "Y", null, null, "hb");

        (await _repository.CountAsync()).Should().Be(2);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // libera o arquivo no Windows antes de apagar
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(path)) File.Delete(path);
    }
}
