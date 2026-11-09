using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Matching;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Valkey;
using StackExchange.Redis;
using Xunit;

namespace Shazamzin.Tests;

/// <summary>Ponta a ponta: HTTP → codec → Valkey (database 15) → scorer → SQLite temporário → JSON.</summary>
[Collection("Valkey database 15")]
public sealed class RealMatchingApiTests : IClassFixture<RealApiFactory>
{
    private readonly RealApiFactory _factory;

    public RealMatchingApiTests(RealApiFactory factory) => _factory = factory;

    private static HttpRequestMessage Post(FingerprintPayload payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/match") { Content = new ByteArrayContent(FingerprintCodec.Encode(payload)) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Add("X-Device-ID", "esp32-e2e");
        request.Headers.Add("X-Api-Key", "dev-key-local");
        return request;
    }

    [ValkeyFact]
    public async Task EndToEnd_IdentifiesTheSeededSong_AndHealthCountsIt()
    {
        using var client = _factory.CreateClient(); // sobe a API: cria o SQLite e aplica o schema
        _factory.InsertSong("Redemption Song", "Bob Marley", "Uprising", 227, "hash-e2e");

        await using var mux = await ConnectionMultiplexer.ConnectAsync(ValkeyMatchingServiceTests.Options);
        await mux.GetServer(mux.GetEndPoints()[0]).FlushDatabaseAsync(15);
        var db = mux.GetDatabase();
        for (var i = 0; i < 200; i++)
            await db.StringAppendAsync(PostingsCodec.KeyFor(5_000_000 + (uint)i), PostingsCodec.EncodeOne(new Posting(1, i * 100)));

        // 80 hashes capturados a partir de 12,3 s da música
        var slice = Enumerable.Range(123, 80)
            .Select(i => new Fingerprint(5_000_000 + (uint)i, (uint)((i - 123) * 100)))
            .ToArray();

        var response = await client.SendAsync(Post(new FingerprintPayload(99, slice)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("match").GetBoolean().Should().BeTrue();
        json.GetProperty("song_id").GetInt32().Should().Be(1);
        json.GetProperty("title").GetString().Should().Be("Redemption Song");
        json.GetProperty("artist").GetString().Should().Be("Bob Marley");
        json.GetProperty("matched_offset_sec").GetDouble().Should().Be(12.3);
        json.GetProperty("confidence").GetDouble().Should().Be(1.0);

        var health = await (await client.GetAsync("/health")).Content.ReadFromJsonAsync<JsonElement>();
        health.GetProperty("status").GetString().Should().Be("ok");
        health.GetProperty("valkey").GetString().Should().Be("up");
        health.GetProperty("sqlite").GetString().Should().Be("up");
        health.GetProperty("indexed_songs").GetInt32().Should().Be(1);

        await mux.GetServer(mux.GetEndPoints()[0]).FlushDatabaseAsync(15);
    }
}

/// <summary>Valkey fora do ar: a API sobe, mas /health vira 503 "degraded" e mostra qual dependência caiu.</summary>
public sealed class ValkeyDownHealthTests : IClassFixture<ValkeyDownApiFactory>
{
    private readonly ValkeyDownApiFactory _factory;

    public ValkeyDownHealthTests(ValkeyDownApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_Returns503Degraded_WhenValkeyIsDown()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("degraded");
        json.GetProperty("valkey").GetString().Should().Be("down");
        json.GetProperty("sqlite").GetString().Should().Be("up");
    }
}
