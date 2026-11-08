using FluentAssertions;
using Shazamzin.Core.Matching;
using Shazamzin.Core.Models;
using Xunit;

namespace Shazamzin.Tests;

public class MatchScorerTests
{
    private const int Bucket = 50;
    private const int MinScore = 8;

    private static Fingerprint[] Query(int count, uint firstHash = 1, uint stepMs = 23) =>
        Enumerable.Range(0, count).Select(i => new Fingerprint(firstHash + (uint)i, (uint)i * stepMs)).ToArray();

    /// <summary>Catálogo em que cada hash da consulta aparece na música no instante timestamp + shiftMs.</summary>
    private static Dictionary<uint, Posting[]> Aligned(IEnumerable<Fingerprint> query, int songId, int shiftMs) =>
        query.ToDictionary(f => f.Hash, f => new[] { new Posting(songId, (int)f.TimestampMs + shiftMs) });

    [Fact]
    public void FindsSong_WhenAllHashesShareTheSameDelta()
    {
        var query = Query(50);

        var result = MatchScorer.Score(query, Aligned(query, songId: 7, shiftMs: 30_000), Bucket, MinScore);

        result.HashesHit.Should().Be(50);
        result.Best.Should().NotBeNull();
        result.Best!.SongId.Should().Be(7);
        result.Best.Score.Should().Be(50);
        result.Best.OffsetSec.Should().BeApproximately(30.0, 1e-9);
        result.Best.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void HandlesNegativeDelta_WhenCaptureStartsAfterSongOffsetZero()
    {
        // consulta com timestamps grandes e música com offsets pequenos => delta negativo
        var query = Enumerable.Range(0, 20).Select(i => new Fingerprint((uint)(100 + i), (uint)(5000 + i * 23))).ToArray();

        var result = MatchScorer.Score(query, Aligned(query, 3, shiftMs: -4_000), Bucket, MinScore);

        result.Best!.SongId.Should().Be(3);
        result.Best.OffsetSec.Should().BeApproximately(-4.0, 1e-9);
    }

    [Fact]
    public void JitterInsideOneBucket_StillCountsAsOnePeak()
    {
        var query = Query(30);
        var postings = query.Select((f, i) => (f, i)).ToDictionary(
            x => x.f.Hash,
            x => new[] { new Posting(1, (int)x.f.TimestampMs + 30_000 + (x.i % 3) * 10) }); // 0, 10, 20 ms

        var result = MatchScorer.Score(query, postings, Bucket, MinScore);

        result.Best!.Score.Should().Be(30);
    }

    [Fact]
    public void ScatteredCollisions_StayBelowThreshold()
    {
        var query = Query(40);
        // cada hash "bate" na mesma música, mas em instantes totalmente diferentes: nenhum pico
        var postings = query.Select((f, i) => (f, i)).ToDictionary(
            x => x.f.Hash,
            x => new[] { new Posting(1, 1_000 + x.i * 997) });

        var result = MatchScorer.Score(query, postings, Bucket, MinScore);

        result.HashesHit.Should().Be(40);
        result.Best.Should().BeNull();
    }

    [Fact]
    public void ScoreJustBelowThreshold_IsNoMatch_AndAtThreshold_IsMatch()
    {
        var query = Query(MinScore);

        MatchScorer.Score(query.Take(MinScore - 1).ToArray(), Aligned(query, 1, 0), Bucket, MinScore).Best.Should().BeNull();
        MatchScorer.Score(query, Aligned(query, 1, 0), Bucket, MinScore).Best.Should().NotBeNull();
    }

    [Fact]
    public void PicksTheSongWithTheHigherPeak()
    {
        var query = Query(60);
        var catalog = Aligned(query, songId: 1, shiftMs: 10_000); // alinha 60
        foreach (var f in query.Take(20))                          // música 2 alinha só 20
            catalog[f.Hash] = catalog[f.Hash].Append(new Posting(2, (int)f.TimestampMs + 500)).ToArray();

        var result = MatchScorer.Score(query, catalog, Bucket, MinScore);

        result.Best!.SongId.Should().Be(1);
        result.Best.Score.Should().Be(60);
    }

    [Fact]
    public void Tie_PrefersLowerSongId()
    {
        var query = Query(10);
        var catalog = query.ToDictionary(
            f => f.Hash,
            f => new[] { new Posting(9, (int)f.TimestampMs), new Posting(4, (int)f.TimestampMs) });

        MatchScorer.Score(query, catalog, Bucket, MinScore).Best!.SongId.Should().Be(4);
    }

    [Fact]
    public void ConfidenceIsFractionOfFoundHashesThatAgree()
    {
        var query = Query(40);
        var catalog = Aligned(query.Take(20), 1, 0); // 20 alinham...
        foreach (var f in query.Skip(20).Take(20))   // ...e 20 acertam o hash, mas em outro lugar
            catalog[f.Hash] = new[] { new Posting(1, 100_000 + (int)f.TimestampMs * 13) };

        var result = MatchScorer.Score(query, catalog, Bucket, MinScore);

        result.HashesHit.Should().Be(40);
        result.Best!.Score.Should().Be(20);
        result.Best.Confidence.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void EmptyQueryOrEmptyCatalog_IsNoMatch()
    {
        MatchScorer.Score(Array.Empty<Fingerprint>(), new Dictionary<uint, Posting[]>(), Bucket, MinScore)
            .Should().Be(new ScoreResult(0, null));

        var result = MatchScorer.Score(Query(20), new Dictionary<uint, Posting[]>(), Bucket, MinScore);
        result.HashesHit.Should().Be(0);
        result.Best.Should().BeNull();
    }

    [Fact]
    public void RejectsNonPositiveBucket()
    {
        var act = () => MatchScorer.Score(Query(1), new Dictionary<uint, Posting[]>(), 0, MinScore);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
