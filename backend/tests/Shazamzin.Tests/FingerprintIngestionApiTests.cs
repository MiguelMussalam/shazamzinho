using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using System.Net.Http.Headers;
using System.Text.Json;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Models;
using StackExchange.Redis;

namespace Shazamzin.Tests;

[Collection("Valkey database 15")]
public sealed class FingerprintIngestionApiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("dev-key-local")]
    public async Task RejectsMissingOrRecognitionKey(string? key)
    {
        using var factory = new IngestionApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/catalog/fingerprints")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("X-Api-Key", "dev-key-local");
        if (key is not null) request.Headers.Add("X-Ingestion-Key", key);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsFingerprintWithMissingHash()
    {
        using var factory = new IngestionApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ingestion-Key", "catalog-test-key");

        using var response = await client.PostAsJsonAsync("/catalog/fingerprints", new
        {
            file_sha256 = new string('a', 64),
            profile = "test-profile",
            title = "Track",
            artist = "Artist",
            fingerprints = new[] { new { timestamp_ms = 0 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [ValkeyFact]
    public async Task RegistersAndRecognizesTheSameSongAcrossRetries()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(ValkeyMatchingServiceTests.Options);
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        await server.FlushDatabaseAsync(15);
        try
        {
            using var factory = new IngestionApiFactory("localhost:6379,defaultDatabase=15");
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Ingestion-Key", "catalog-test-key");

            var entries = Enumerable.Range(0, 80)
                .Select(i => new { hash = 5_000_000 + i, timestamp_ms = 1000 + 100 * i })
                .ToArray();
            var registration = new
            {
                file_sha256 = new string('b', 64), profile = "test-profile",
                title = "Faixa de teste", artist = "Artista", fingerprints = entries
            };

            using var created = await client.PostAsJsonAsync("/catalog/fingerprints", registration);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var song = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("song_id").GetInt32();
            using var repeated = await client.PostAsJsonAsync("/catalog/fingerprints", registration);
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            var retry = await repeated.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(retry.GetProperty("skipped").GetBoolean());
            Assert.Equal(song, retry.GetProperty("song_id").GetInt32());

            var query = new FingerprintPayload(1, entries.Select(entry =>
                new Fingerprint((uint)entry.hash, (uint)(entry.timestamp_ms - 1000))).ToArray());
            using var request = new HttpRequestMessage(HttpMethod.Post, "/match")
            {
                Content = new ByteArrayContent(FingerprintCodec.Encode(query))
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Headers.Add("X-Api-Key", "dev-key-local");
            using var match = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, match.StatusCode);
            var result = await match.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.GetProperty("match").GetBoolean());
            Assert.Equal(song, result.GetProperty("song_id").GetInt32());
        }
        finally
        {
            await server.FlushDatabaseAsync(15);
        }
    }

    private sealed class IngestionApiFactory : RealApiFactory
    {
        public IngestionApiFactory(string valkey = "localhost:6399,defaultDatabase=15") : base(valkey) { }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:ApiKey", "dev-key-local");
            builder.UseSetting("Ingestion:ApiKey", "catalog-test-key");
            builder.UseSetting("Ingestion:Profile", "test-profile");
        }
    }
}
