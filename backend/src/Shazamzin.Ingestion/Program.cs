using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Ingestion;
using Shazamzin.Infrastructure.Native;
using Shazamzin.Infrastructure.Sqlite;
using StackExchange.Redis;

var root = new RootCommand("Shazamzin — ingestão de um arquivo e extração de consultas reais (Olaf + FFmpeg).");
var add = new Command("add", "Indexa um arquivo. Repetir o mesmo arquivo não duplica; uma tentativa incompleta é reparada.");
var path = new Option<string>("--path") { Required = true, Description = "Arquivo de áudio (pastas entram na próxima entrega)." };
var title = new Option<string?>("--title") { Description = "Título; padrão: tag ou nome do arquivo." };
var artist = new Option<string?>("--artist") { Description = "Artista; padrão: tag ou Artista desconhecido." };
var album = new Option<string?>("--album") { Description = "Álbum; padrão: tag do arquivo." };
var sqlite = new Option<string>("--sqlite")
{
    Description = "Connection string SQLite. Caminhos relativos usam a pasta backend.",
    DefaultValueFactory = _ => Environment.GetEnvironmentVariable("Sqlite__ConnectionString") ?? "Data Source=./data/shazamzin.db"
};
var valkey = new Option<string>("--valkey")
{
    Description = "Connection string Valkey, incluindo defaultDatabase se necessário.",
    DefaultValueFactory = _ => Environment.GetEnvironmentVariable("Valkey__ConnectionString") ?? "localhost:6379"
};
foreach (var option in new Option[] { path, title, artist, album, sqlite, valkey }) add.Options.Add(option);
add.SetAction(async (parse, cancellationToken) => await RunAsync(async () =>
{
    var audioPath = Path.GetFullPath(parse.GetValue(path)!);
    await using var source = new FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
    var profile = OlafNative.Profile;
    Console.Error.WriteLine("Decodificando áudio e extraindo fingerprints...");
    var samples = await AudioDecoder.DecodeAsync(audioPath, cancellationToken: cancellationToken);
    var fingerprints = OlafNative.Extract(samples, cancellationToken);
    if (fingerprints.Count == 0) throw new InvalidOperationException("Áudio sem fingerprints: silêncio ou trecho curto demais.");
    var metadata = await AudioDecoder.ReadMetadataAsync(audioPath, cancellationToken);
    metadata = metadata with
    {
        Title = Override(parse.GetValue(title), metadata.Title),
        Artist = Override(parse.GetValue(artist), metadata.Artist),
        Album = parse.GetValue(album)?.Trim() ?? metadata.Album
    };
    var sql = SqliteInitializer.ResolveConnectionString(parse.GetValue(sqlite)!, AppContext.BaseDirectory);
    var redisOptions = ConfigurationOptions.Parse(parse.GetValue(valkey)!);
    redisOptions.AbortOnConnectFail = true;
    redisOptions.ConnectTimeout = 3000;
    redisOptions.ConnectRetry = 1;
    await using var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
    var result = await new CatalogWriter(sql, redis).AddAsync(audioPath, hash, metadata, fingerprints, profile, cancellationToken);
    Print(new { result.SongId, result.Fingerprints, result.Skipped, Profile = profile });
}));

var extract = new Command("extract", "Decodifica um trecho separadamente e gera um payload .bin para POST /match.");
var extractPath = new Option<string>("--path") { Required = true, Description = "Arquivo de áudio de origem." };
var output = new Option<string>("--output") { Required = true, Description = "Arquivo .bin de saída; não sobrescreve arquivos existentes." };
var offset = new Option<double>("--offset-seconds") { Description = "Início do trecho em segundos (padrão 0)." };
var duration = new Option<double?>("--duration-seconds") { Description = "Duração do trecho em segundos; omitido = até o fim." };
var session = new Option<uint>("--session-id") { DefaultValueFactory = _ => 1, Description = "ID da sessão no protocolo v1." };
var rawOutput = new Option<string?>("--raw-output") { Description = "Opcional: salva PCM float32 LE para comparar com a CLI C." };
foreach (var option in new Option[] { extractPath, output, offset, duration, session, rawOutput }) extract.Options.Add(option);
extract.SetAction(async (parse, cancellationToken) => await RunAsync(async () =>
{
    var outputPath = Path.GetFullPath(parse.GetValue(output)!);
    var rawPath = parse.GetValue(rawOutput) is { } raw ? Path.GetFullPath(raw) : null;
    if (File.Exists(outputPath) || rawPath is not null && File.Exists(rawPath))
        throw new IOException("Arquivo de saída já existe. Escolha outro nome.");
    if (string.Equals(outputPath, rawPath, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("--output e --raw-output precisam de caminhos diferentes.");
    var samples = await AudioDecoder.DecodeAsync(parse.GetValue(extractPath)!, parse.GetValue(offset),
        parse.GetValue(duration), cancellationToken);
    var fingerprints = OlafNative.Extract(samples, cancellationToken);
    if (fingerprints.Count == 0) throw new InvalidOperationException("Trecho sem fingerprints: silêncio ou duração insuficiente.");
    var payload = FingerprintCodec.Encode(new FingerprintPayload(parse.GetValue(session), fingerprints));
    await WriteNewAsync(outputPath, payload, cancellationToken);
    if (rawPath is not null)
    {
        var pcm = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
        await WriteNewAsync(rawPath, pcm, cancellationToken);
    }
    Print(new { Output = outputPath, Fingerprints = fingerprints.Count, Bytes = payload.Length,
        DurationSec = samples.Length / (double)OlafNative.SampleRate, Profile = OlafNative.Profile });
}));
root.Subcommands.Add(add);
root.Subcommands.Add(extract);
return await root.Parse(args).InvokeAsync();

static string Override(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value,
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true }));
static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken cancellationToken)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    await file.WriteAsync(bytes, cancellationToken);
}
static async Task<int> RunAsync(Func<Task> action)
{
    try { await action(); return 0; }
    catch (OperationCanceledException) { Console.Error.WriteLine("Operação cancelada."); return 130; }
    catch (DllNotFoundException) { Console.Error.WriteLine("Biblioteca Olaf ausente. Rode native/build.ps1 e depois dotnet build."); return 1; }
    catch (Exception error) { Console.Error.WriteLine($"Erro: {error.Message}"); return 1; }
}
