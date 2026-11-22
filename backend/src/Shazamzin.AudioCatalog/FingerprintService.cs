using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shazamzin.AudioFingerprinting;
using Shazamzin.Core.Matching;
using Shazamzin.Core.Models;

namespace Shazamzin.AudioCatalog;

public sealed class FingerprintService
{
    private readonly IAudioInputNormalizer _normalizer;
    private readonly AudioFingerprinter _fingerprinter;
    private readonly ITrackRepository _tracks;
    private readonly IdentificationOptions _options;
    private readonly IFingerprintIndex _index;
    private readonly ILogger<FingerprintService> _logger;

    public FingerprintService(IAudioInputNormalizer normalizer, AudioFingerprinter fingerprinter,
        ITrackRepository tracks, IdentificationOptions options, IFingerprintIndex? index = null,
        ILogger<FingerprintService>? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MinScore);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DeltaBucketMs);
        _normalizer = normalizer;
        _fingerprinter = fingerprinter;
        _tracks = tracks;
        _options = options;
        _index = index ?? new SqliteFingerprintIndex(tracks);
        _logger = logger ?? NullLogger<FingerprintService>.Instance;
    }

    public Task<int> RegisterTrackAsync(
        ReadOnlyMemory<byte> audio, string title, string? artist, CancellationToken cancellationToken) =>
        RegisterTrackAsync(audio, title, artist, null, cancellationToken);

    public async Task<int> RegisterTrackAsync(
        ReadOnlyMemory<byte> audio, string title, string? artist, string? formatHint, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var (hashes, durationMs) = await ExtractFingerprintsAsync(audio, formatHint, "register", cancellationToken);
        if (hashes.Length == 0)
            throw new ArgumentException("The audio did not produce any fingerprints.", nameof(audio));

        var storedHashes = hashes.Select(fingerprint => (fingerprint.Hash, (int)fingerprint.TimestampMs)).ToArray();
        var trackId = await _tracks.AddTrackAsync(title.Trim(), string.IsNullOrWhiteSpace(artist) ? null : artist.Trim(),
            durationMs, storedHashes, cancellationToken);

        try
        {
            await _index.IndexAsync(trackId, storedHashes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Track {TrackId} was saved in SQLite before indexing was canceled", trackId);
        }

        return trackId;
    }

    public Task<IdentifyResult?> IdentifyAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken) =>
        IdentifyAsync(audio, null, cancellationToken);

    public async Task<IdentifyResult?> IdentifyAsync(
        ReadOnlyMemory<byte> audio, string? formatHint, CancellationToken cancellationToken)
    {
        var (query, _) = await ExtractFingerprintsAsync(audio, formatHint, "identify", cancellationToken);
        if (query.Length == 0)
        {
            _logger.LogInformation("Identification finished with score {Score} and matched {Matched}", 0, false);
            return null;
        }

        var hashes = query.Select(fingerprint => fingerprint.Hash).Distinct().ToArray();
        var lookupStarted = Stopwatch.GetTimestamp();
        var hits = await _index.LookupAsync(hashes, cancellationToken);
        _logger.LogInformation("Fingerprint lookup completed in {LookupMs} ms for {LookupHashes} hashes",
            Stopwatch.GetElapsedTime(lookupStarted).TotalMilliseconds, hashes.Length);
        var postings = hits.ToDictionary(pair => pair.Key,
            pair => pair.Value.Select(hit => new Posting(hit.TrackId, hit.Time)).ToArray());

        cancellationToken.ThrowIfCancellationRequested();
        var score = MatchScorer.Score(query, postings, _options.DeltaBucketMs, _options.MinScore);
        IdentifyResult? result = null;
        if (score.Best is { } best && await _tracks.GetTrackAsync(best.SongId, cancellationToken) is { } song)
            result = new IdentifyResult(song.Title, song.Artist, best.Score, best.OffsetSec);

        _logger.LogInformation("Identification finished with score {Score} and matched {Matched}", result?.Score ?? 0, result is not null);
        return result;
    }

    private async Task<(Fingerprint[] Hashes, int DurationMs)> ExtractFingerprintsAsync(
        ReadOnlyMemory<byte> audio, string? formatHint, string operation, CancellationToken cancellationToken)
    {
        var normalizationStarted = Stopwatch.GetTimestamp();
        var samples = await _normalizer.NormalizeAsync(audio, formatHint, cancellationToken);
        var normalizationMs = Stopwatch.GetElapsedTime(normalizationStarted).TotalMilliseconds;

        var peaks = _fingerprinter.ExtractPeaks(samples);
        cancellationToken.ThrowIfCancellationRequested();

        var fingerprints = new HashSet<Fingerprint>();
        foreach (var (hash, frame) in _fingerprinter.GenerateHashes(peaks))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timeMs = SamplesToMilliseconds((long)frame * FingerprintOptions.Hop);
            fingerprints.Add(new Fingerprint(hash, (uint)timeMs));
        }

        _logger.LogInformation(
            "Audio preparation for {Operation} took {NormalizationMs} ms to normalize and produced {Peaks} peaks and {Hashes} hashes",
            operation, normalizationMs, peaks.Count, fingerprints.Count);
        return (fingerprints.ToArray(), SamplesToMilliseconds(samples.Length));
    }

    private static int SamplesToMilliseconds(long samples) =>
        checked((int)(samples * 1000 / FingerprintOptions.TargetSampleRate));
}
