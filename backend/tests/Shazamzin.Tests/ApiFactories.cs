using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Shazamzin.Tests;

/// <summary>API com o matching fake da Fase 1: sem Valkey nem SQLite.</summary>
public sealed class FakeApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Matching:Provider", "Fake");
}

/// <summary>
/// API com o matching real. Valkey no database 15 (o do catálogo de verdade é o 0) e um SQLite
/// temporário que a própria API cria na inicialização. Porta configurável para simular Valkey fora do ar.
/// </summary>
public class RealApiFactory : WebApplicationFactory<Program>
{
    private readonly string _valkey;

    public RealApiFactory() : this("localhost:6379,defaultDatabase=15") { }
    protected RealApiFactory(string valkey) => _valkey = valkey;

    public string SqlitePath { get; } = Path.Combine(Path.GetTempPath(), $"shazamzin-api-{Guid.NewGuid():N}.db");
    public string SqliteConnectionString => $"Data Source={SqlitePath}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Matching:Provider", "Valkey");
        builder.UseSetting("Valkey:ConnectionString", _valkey);
        builder.UseSetting("Sqlite:ConnectionString", SqliteConnectionString);
    }

    public void InsertSong(string title, string artist, string? album, int durationSec, string fileHash)
    {
        using var connection = new SqliteConnection(SqliteConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO songs (title, artist, album, duration_sec, file_path, file_hash) VALUES ($t, $a, $al, $d, 'x.mp3', $h)";
        command.Parameters.AddWithValue("$t", title);
        command.Parameters.AddWithValue("$a", artist);
        command.Parameters.AddWithValue("$al", (object?)album ?? DBNull.Value);
        command.Parameters.AddWithValue("$d", durationSec);
        command.Parameters.AddWithValue("$h", fileHash);
        command.ExecuteNonQuery();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { SqlitePath, SqlitePath + "-wal", SqlitePath + "-shm" })
            if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>Valkey apontando para uma porta sem nada escutando.</summary>
public sealed class ValkeyDownApiFactory : RealApiFactory
{
    public ValkeyDownApiFactory() : base("localhost:6399,defaultDatabase=15") { }
}
