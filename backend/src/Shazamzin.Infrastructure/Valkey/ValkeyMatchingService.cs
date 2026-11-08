using System.Diagnostics;
using Shazamzin.Core.Interfaces;
using Shazamzin.Core.Matching;
using Shazamzin.Core.Models;
using StackExchange.Redis;

namespace Shazamzin.Infrastructure.Valkey;

/// <summary>
/// Matching real: um MGET em lote busca os postings de todos os hashes distintos da consulta,
/// o <see cref="MatchScorer"/> acha o pico do histograma de deltas e o SQLite resolve os metadados.
/// </summary>
public sealed class ValkeyMatchingService : IMatchingService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ISongRepository _songs;
    private readonly MatchingOptions _options;

    public ValkeyMatchingService(IConnectionMultiplexer redis, ISongRepository songs, MatchingOptions options)
    {
        _redis = redis;
        _songs = songs;
        _options = options;
    }

    public async Task<MatchResult> MatchAsync(FingerprintPayload payload, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var received = payload.Fingerprints.Count;

        var postingsByHash = await LoadPostingsAsync(payload.Fingerprints);
        var scored = MatchScorer.Score(payload.Fingerprints, postingsByHash, _options.DeltaBucketMs, _options.MinScoreThreshold);

        if (scored.Best is { } best)
        {
            var song = await _songs.GetByIdAsync(best.SongId, cancellationToken);
            if (song is not null)
            {
                return new MatchResult
                {
                    Match = true,
                    SongId = song.Id,
                    Title = song.Title,
                    Artist = song.Artist,
                    Album = song.Album,
                    Confidence = Math.Round(best.Confidence, 2),
                    MatchedOffsetSec = Math.Round(best.OffsetSec, 1),
                    ProcessingTimeMs = ElapsedMs(started),
                };
            }
            // Índice aponta para uma música que não está no SQLite (dessincronia): tratar como no-match.
            // O comando 'verify' da ingestão (Fase 3) existe para achar esse caso.
        }

        return new MatchResult
        {
            Match = false,
            HashesReceived = received,
            HashesHit = scored.HashesHit,
            ProcessingTimeMs = ElapsedMs(started),
        };
    }

    private async Task<Dictionary<uint, Posting[]>> LoadPostingsAsync(IReadOnlyList<Fingerprint> query)
    {
        var distinct = query.Select(f => f.Hash).Distinct().ToArray();
        var result = new Dictionary<uint, Posting[]>(distinct.Length);
        if (distinct.Length == 0) return result;

        var keys = distinct.Select(h => (RedisKey)PostingsCodec.KeyFor(h)).ToArray();
        var values = await _redis.GetDatabase().StringGetAsync(keys);

        for (var i = 0; i < distinct.Length; i++)
        {
            if (values[i].IsNullOrEmpty) continue;
            result[distinct[i]] = PostingsCodec.Decode((byte[])values[i]!);
        }
        return result;
    }

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
