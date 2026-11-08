using System.Diagnostics;
using Shazamzin.Core.Interfaces;
using Shazamzin.Core.Models;

namespace Shazamzin.Infrastructure.Matching;

/// <summary>
/// Implementação fake da Fase 1: não consulta Valkey nem SQLite.
/// Com pelo menos <see cref="MinHashesForFakeMatch"/> hashes devolve sempre a mesma música;
/// com menos, devolve no-match. Existe só para exercitar o contrato ponta a ponta
/// antes do matching real (Fase 2).
/// </summary>
public sealed class FakeMatchingService : IMatchingService
{
    public const int MinHashesForFakeMatch = 10;

    public Task<MatchResult> MatchAsync(FingerprintPayload payload, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var received = payload.Fingerprints.Count;

        MatchResult result = received >= MinHashesForFakeMatch
            ? new MatchResult
            {
                Match = true,
                SongId = 42,
                Title = "Redemption Song",
                Artist = "Bob Marley",
                Album = "Uprising",
                Confidence = 0.87,
                MatchedOffsetSec = 47.3,
                ProcessingTimeMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            }
            : new MatchResult
            {
                Match = false,
                HashesReceived = received,
                HashesHit = 0,
                ProcessingTimeMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            };

        return Task.FromResult(result);
    }
}
