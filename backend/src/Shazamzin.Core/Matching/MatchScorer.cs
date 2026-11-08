using Shazamzin.Core.Models;

namespace Shazamzin.Core.Matching;

/// <param name="HashesHit">Quantos fingerprints da consulta têm ao menos uma ocorrência no catálogo.</param>
/// <param name="Best">Melhor candidato, ou null se nenhum atingiu o limiar.</param>
public sealed record ScoreResult(int HashesHit, SongScore? Best);

/// <param name="Score">Hashes alinhados no pico do histograma.</param>
/// <param name="OffsetSec">Posição na música (s) em que a captura começou.</param>
/// <param name="Confidence">Score / HashesHit, limitado a 1: fração dos hashes encontrados que concordam no alinhamento.</param>
public sealed record SongScore(int SongId, int Score, double OffsetSec, double Confidence);

/// <summary>
/// Scoring por histograma de deltas (Wang 2003). Cada par (hash da consulta, ocorrência no catálogo)
/// vota em delta = offset_na_música − timestamp_na_consulta. Capturas da mesma música produzem o
/// mesmo delta para muitos hashes; colisões ao acaso espalham os votos. O pico por música é o score.
/// Função pura: não toca Valkey nem SQLite.
/// </summary>
public static class MatchScorer
{
    public static ScoreResult Score(
        IReadOnlyList<Fingerprint> query,
        IReadOnlyDictionary<uint, Posting[]> postingsByHash,
        int deltaBucketMs,
        int minScore)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(postingsByHash);
        if (deltaBucketMs <= 0) throw new ArgumentOutOfRangeException(nameof(deltaBucketMs));

        var histogram = new Dictionary<(int SongId, long Bucket), (int Count, long SumDeltaMs)>();
        var hashesHit = 0;

        foreach (var fingerprint in query)
        {
            if (!postingsByHash.TryGetValue(fingerprint.Hash, out var postings) || postings.Length == 0)
                continue;

            hashesHit++;
            foreach (var posting in postings)
            {
                var delta = (long)posting.OffsetMs - fingerprint.TimestampMs;
                var key = (posting.SongId, FloorDiv(delta, deltaBucketMs));
                histogram.TryGetValue(key, out var cell);
                histogram[key] = (cell.Count + 1, cell.SumDeltaMs + delta);
            }
        }

        if (histogram.Count == 0)
            return new ScoreResult(hashesHit, null);

        // Desempate determinístico: mais votos, depois menor song_id, depois menor bucket.
        var best = histogram
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key.SongId)
            .ThenBy(kv => kv.Key.Bucket)
            .First();

        if (best.Value.Count < minScore)
            return new ScoreResult(hashesHit, null);

        var offsetSec = best.Value.SumDeltaMs / (double)best.Value.Count / 1000.0;
        var confidence = Math.Min(1.0, best.Value.Count / (double)hashesHit);
        return new ScoreResult(hashesHit, new SongScore(best.Key.SongId, best.Value.Count, offsetSec, confidence));
    }

    /// <summary>Divisão inteira arredondando para −∞ (o delta pode ser negativo).</summary>
    private static long FloorDiv(long value, long divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
