using System.CommandLine;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Shazamzin.AudioCatalog;
using Shazamzin.AudioFingerprinting;
using StackExchange.Redis;

var root = new RootCommand("Register audio files and identify excerpts in a local SQLite catalog.");
var register = new Command("register", "Add a track and its fingerprints to the catalog.");
var identify = new Command("identify", "Find the catalog track matching an audio excerpt.");

var audio = new Argument<FileInfo>("audio") { Description = "Audio file to read." };
var title = new Option<string>("--title") { Required = true, Description = "Track title." };
var artist = new Option<string?>("--artist") { Description = "Artist name." };
register.Arguments.Add(audio);
register.Options.Add(title);
register.Options.Add(artist);
identify.Arguments.Add(audio);

var database = new Option<FileInfo?>("--database")
{
    Description = "SQLite catalog path. By default, use ConnectionStrings:AudioCatalog from configuration."
};
var format = new Option<string?>("--format") { Description = "Override audio format detection, e.g. f32le-8000-mono." };
root.Options.Add(database);
root.Options.Add(format);
database.Recursive = true;
format.Recursive = true;
root.Subcommands.Add(register);
root.Subcommands.Add(identify);

register.SetAction((parse, cancellationToken) => RunAsync(parse, async (service, bytes, hint, ct) => new
{
    TrackId = await service.RegisterTrackAsync(bytes, parse.GetValue(title)!, parse.GetValue(artist), hint, ct)
}, cancellationToken));
identify.SetAction((parse, cancellationToken) => RunAsync(parse,
    async (service, bytes, hint, ct) => await service.IdentifyAsync(bytes, hint, ct), cancellationToken));

return await root.Parse(args).InvokeAsync();

async Task<int> RunAsync(ParseResult parse,
    Func<FingerprintService, byte[], string?, CancellationToken, Task<object?>> action,
    CancellationToken cancellationToken)
{
    try
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        var connectionString = parse.GetValue(database) is { } file
            ? new SqliteConnectionStringBuilder { DataSource = file.FullName }.ToString()
            : builder.Configuration.GetConnectionString("AudioCatalog");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Specify --database or configure ConnectionStrings:AudioCatalog.");

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "O";
        });
        builder.Services.Configure<ConsoleLoggerOptions>(options =>
            options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(new SqliteConnectionFactory(connectionString));
        builder.Services.AddSingleton<ITrackRepository, SqliteTrackRepository>();
        builder.Services.AddSingleton<IAudioInputNormalizer, AudioInputNormalizer>();
        builder.Services.AddSingleton(new AudioFingerprinter());
        builder.Services.AddSingleton(builder.Configuration.GetSection(IdentificationOptions.SectionName)
            .Get<IdentificationOptions>() ?? new IdentificationOptions());
        ConfigureFingerprintIndex(builder);
        builder.Services.AddSingleton<FingerprintService>();

        using var host = builder.Build();
        var service = host.Services.GetRequiredService<FingerprintService>();
        var bytes = await File.ReadAllBytesAsync(parse.GetValue(audio)!.FullName, cancellationToken);
        await host.Services.GetRequiredService<SqliteConnectionFactory>().InitializeAsync(cancellationToken);
        var result = await action(service, bytes, parse.GetValue(format), cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Operation canceled.");
        return 130;
    }
    catch (Exception error) when (error is ArgumentException or FormatException or NotSupportedException or
        InvalidOperationException or IOException or UnauthorizedAccessException or SqliteException)
    {
        Console.Error.WriteLine(error.Message);
        return 1;
    }
}

static void ConfigureFingerprintIndex(HostApplicationBuilder builder)
{
    var connectionString = builder.Configuration["Valkey:ConnectionString"];
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        builder.Services.AddSingleton<IFingerprintIndex, SqliteFingerprintIndex>();
        return;
    }

    var options = ConfigurationOptions.Parse(connectionString);
    options.AbortOnConnectFail = false;
    options.ConnectRetry = 0;
    options.ConnectTimeout = 1000;
    options.AsyncTimeout = 2000;
    options.BacklogPolicy = BacklogPolicy.FailFast;
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
    builder.Services.AddSingleton<IndexRebuilder>();
    builder.Services.AddSingleton<ValkeyFingerprintIndex>();
    builder.Services.AddSingleton<IFingerprintIndex>(services =>
    {
        try
        {
            return services.GetRequiredService<ValkeyFingerprintIndex>();
        }
        catch (RedisException error)
        {
            services.GetRequiredService<ILogger<FingerprintService>>().LogWarning(
                "The Valkey connection could not be opened ({ErrorType}). Using SQLite", error.GetType().Name);
            return new SqliteFingerprintIndex(services.GetRequiredService<ITrackRepository>());
        }
    });
}
