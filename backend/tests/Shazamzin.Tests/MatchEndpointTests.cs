using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Models;
using Xunit;

namespace Shazamzin.Tests;

public class MatchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ApiKey = "dev-key-local"; // appsettings.json -> Auth:ApiKey

    private readonly HttpClient _client;

    public MatchEndpointTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    private static byte[] Payload(int hashes) => FingerprintCodec.Encode(new FingerprintPayload(
        7, Enumerable.Range(0, hashes).Select(i => new Fingerprint((uint)(1000 + i), (uint)(i * 23))).ToArray()));

    private static HttpRequestMessage Post(byte[] body, string? apiKey = ApiKey, string contentType = "application/octet-stream")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/match") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Headers.Add("X-Device-ID", "esp32-test");
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }

    [Fact]
    public async Task Health_IsOpenAndReportsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("ok");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("chave-errada")]
    public async Task Match_RejectsMissingOrWrongApiKey(string? key)
    {
        var response = await _client.SendAsync(Post(Payload(20), key));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Match_ReturnsMatchJsonInSnakeCase()
    {
        var response = await _client.SendAsync(Post(Payload(180)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("match").GetBoolean().Should().BeTrue();
        json.GetProperty("song_id").GetInt32().Should().Be(42);
        json.GetProperty("title").GetString().Should().Be("Redemption Song");
        json.GetProperty("artist").GetString().Should().Be("Bob Marley");
        json.GetProperty("album").GetString().Should().Be("Uprising");
        json.GetProperty("confidence").GetDouble().Should().BeApproximately(0.87, 1e-9);
        json.GetProperty("matched_offset_sec").GetDouble().Should().BeApproximately(47.3, 1e-9);
        json.TryGetProperty("processing_time_ms", out _).Should().BeTrue();
        json.TryGetProperty("hashes_received", out _).Should().BeFalse("campos de no-match não vão no JSON de match");
    }

    [Fact]
    public async Task Match_ReturnsNoMatchWithHashCounts()
    {
        var response = await _client.SendAsync(Post(Payload(3)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("match").GetBoolean().Should().BeFalse();
        json.GetProperty("hashes_received").GetInt32().Should().Be(3);
        json.GetProperty("hashes_hit").GetInt32().Should().Be(0);
        json.TryGetProperty("title", out _).Should().BeFalse("campos nulos são omitidos");
    }

    [Fact]
    public async Task Match_RejectsMalformedPayloadWith400()
    {
        var truncated = Payload(5)[..^1];

        var response = await _client.SendAsync(Post(truncated));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("error").GetString().Should().Contain("não bate");
    }

    [Fact]
    public async Task Match_RejectsWrongContentTypeWith415()
    {
        var response = await _client.SendAsync(Post(Payload(5), contentType: "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Match_RejectsOversizedBodyWith413()
    {
        var response = await _client.SendAsync(Post(new byte[FingerprintCodec.MaxPayloadSize + 1]));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }
}
