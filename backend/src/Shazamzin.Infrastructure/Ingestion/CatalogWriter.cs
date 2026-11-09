using Dapper;
using Microsoft.Data.Sqlite;
using Shazamzin.Core.Matching;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Native;
using Shazamzin.Infrastructure.Sqlite;
using Shazamzin.Infrastructure.Valkey;
using StackExchange.Redis;

namespace Shazamzin.Infrastructure.Ingestion;

public sealed record IngestionResult(int SongId, int Fingerprints, bool Skipped);

/// <summary>Escritor local sequencial. Falhas ficam registradas e são reparadas repetindo add.</summary>
public sealed class CatalogWriter(string connectionString, IConnectionMultiplexer redis)
{
    // Troca somente os postings desta música: repetir um lote após uma falha não duplica entradas.
    private const string ReplaceSongPostings = """
        local old = redis.call('GET', KEYS[1]) or ''
        if string.len(old) % 8 ~= 0 then return redis.error_reply('postings corrompidos') end
        local parts = {}
        for i = 1, string.len(old), 8 do
            if string.sub(old, i, i + 3) ~= ARGV[1] then
                parts[#parts + 1] = string.sub(old, i, i + 7)
            end
        end
        parts[#parts + 1] = ARGV[2]
        redis.call('SET', KEYS[1], table.concat(parts))
        return 1
        """;

    public async Task<IngestionResult> AddAsync(string path, string fileHash, AudioMetadata metadata,
        IReadOnlyList<Fingerprint> fingerprints, string profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fingerprints.Count == 0) throw new InvalidOperationException("Áudio sem fingerprints: silêncio ou trecho curto demais.");
        if (fingerprints.Any(f => f.TimestampMs > int.MaxValue))
            throw new ArgumentException("Timestamp excede o limite int32 do índice Valkey.", nameof(fingerprints));
        SqliteInitializer.EnsureCreated(connectionString);
        var dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;
        // O SO libera o lock mesmo se o processo cair. Uma instância por catálogo nesta primeira entrega.
        await using var fileLock = new FileStream(dbPath + ".ingestion.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var sql = new SqliteConnection(connectionString);
        await sql.OpenAsync(cancellationToken);
        await sql.ExecuteAsync("PRAGMA foreign_keys = ON");
        await EnsureIdentityAsync(sql, profile);

        var existing = await sql.QuerySingleOrDefaultAsync<SongState>("""
            SELECT id AS Id, fingerprint_count AS Count,
                EXISTS(SELECT 1 FROM ingestion_log WHERE song_id = songs.id AND status IN ('pending','failed')) AS Incomplete
            FROM songs WHERE file_hash = @fileHash
            """, new { fileHash });
        if (existing is { Incomplete: false, Count: > 0 })
            return new IngestionResult(existing.Id, existing.Count.Value, true);

        int songId;
        using (var transaction = sql.BeginTransaction())
        {
            songId = existing?.Id ?? await sql.ExecuteScalarAsync<int>("""
                INSERT INTO songs (title,artist,album,duration_sec,file_path,file_hash,fingerprint_count)
                VALUES (@Title,@Artist,@Album,@DurationSec,@path,@fileHash,0) RETURNING id
                """, new { metadata.Title, metadata.Artist, metadata.Album, metadata.DurationSec, path, fileHash }, transaction);
            await sql.ExecuteAsync("""
                INSERT INTO ingestion_log (file_path,file_hash,song_id,started_at,status)
                VALUES (@path,@fileHash,@songId,datetime('now'),'pending')
                """, new { path, fileHash, songId }, transaction);
            transaction.Commit(); // Reserva o ID de forma permanente antes de escrever no índice externo.
        }
        try
        {
            var db = redis.GetDatabase();
            var idBytes = PostingsCodec.EncodeOne(new Posting(songId, 0))[..4];
            foreach (var chunk in fingerprints.Distinct().GroupBy(f => f.Hash).Chunk(128))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tasks = chunk.Select(group => db.ScriptEvaluateAsync(ReplaceSongPostings,
                    [PostingsCodec.KeyFor(group.Key)],
                    [idBytes, PostingsCodec.Encode(group.Select(f => new Posting(songId, checked((int)f.TimestampMs))).ToArray())]));
                // Aguarda todos os writes enviados, inclusive quando um deles falha.
                await Task.WhenAll(tasks);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var transaction = sql.BeginTransaction();
            await sql.ExecuteAsync("UPDATE songs SET fingerprint_count = @count WHERE id = @songId",
                new { count = fingerprints.Distinct().Count(), songId }, transaction);
            await sql.ExecuteAsync("""
                UPDATE ingestion_log SET status='done',finished_at=datetime('now'),error_message=NULL
                WHERE song_id=@songId AND status IN ('pending','failed')
                """, new { songId }, transaction);
            transaction.Commit();
            return new IngestionResult(songId, fingerprints.Distinct().Count(), false);
        }
        catch (Exception error)
        {
            try
            {
                await sql.ExecuteAsync("""
                    UPDATE ingestion_log SET status='failed',finished_at=datetime('now'),error_message=@message
                    WHERE song_id=@songId AND status='pending'
                    """, new { songId, message = error.Message });
            }
            catch (Exception logError) { error.Data["IngestionLogError"] = logError.Message; }
            throw;
        }
    }

    private async Task EnsureIdentityAsync(SqliteConnection sql, string profile)
    {
        var identity = await sql.QuerySingleOrDefaultAsync<string>("SELECT value FROM catalog_settings WHERE key='identity'");
        var songs = await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM songs");
        var db = redis.GetDatabase();
        RedisValue storedIdentity = await db.StringGetAsync("shazamzin:catalog");
        if (identity is null)
        {
            if (songs > 0 || !storedIdentity.IsNull)
                throw new InvalidOperationException("Catálogo sem perfil compatível. Use SQLite e Valkey vazios e separados; os dados existentes não serão alterados.");
            var server = redis.GetServer(redis.GetEndPoints().First());
            if (server.Keys(db.Database, "fp:*", pageSize: 10).Any())
                throw new InvalidOperationException("Valkey já contém fingerprints sem identificação. Selecione um banco vazio.");
            identity = profile + "|" + Guid.NewGuid().ToString("N");
            await sql.ExecuteAsync("INSERT INTO catalog_settings(key,value) VALUES ('identity',@identity)", new { identity });
        }
        if (!identity.StartsWith(profile + "|", StringComparison.Ordinal))
            throw new InvalidOperationException("Perfil Olaf incompatível com este catálogo. Use outro catálogo ou reindexe explicitamente.");
        if (storedIdentity.IsNull)
        {
            if (songs > 0) throw new InvalidOperationException("Índice Valkey sem identidade, mas SQLite contém músicas. Verifique a conexão; não é seguro continuar.");
            await db.StringSetAsync("shazamzin:catalog", identity, when: When.NotExists);
            storedIdentity = await db.StringGetAsync("shazamzin:catalog");
        }
        if ((string?)storedIdentity != identity)
            throw new InvalidOperationException("Este SQLite pertence a outro índice Valkey. Confira --sqlite e --valkey.");
    }

    private sealed class SongState
    {
        public int Id { get; set; }
        public int? Count { get; set; }
        public bool Incomplete { get; set; }
    }
}
