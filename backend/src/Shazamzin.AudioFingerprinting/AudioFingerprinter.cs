using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Shazamzin.AudioFingerprinting;

public sealed class AudioFingerprinter
{
    private const int FrequencyBins = FingerprintOptions.WindowSize / 2;
    private const int MaxEncodedDelta = byte.MaxValue;
    private static readonly double[] HannWindow = CreateHannWindow();
    private readonly FingerprintOptions _options;
    private readonly int[] _bandEdges;

    public AudioFingerprinter(FingerprintOptions? options = null)
    {
        _options = options ?? new FingerprintOptions();
        _bandEdges = _options.BandEdges.ToArray();
        if (_bandEdges.Length < 2 || _bandEdges[0] < 1 || _bandEdges[^1] > FrequencyBins ||
            _bandEdges.Zip(_bandEdges.Skip(1)).Any(pair => pair.First >= pair.Second))
        {
            throw new ArgumentException("Band edges must be strictly increasing and between 1 and 1024.", nameof(options));
        }

        if (!double.IsFinite(_options.PeakThreshold) || _options.PeakThreshold < 1)
            throw new ArgumentException("The peak threshold must be finite and at least 1.", nameof(options));

        ValidatePairing(_options.FanOut, _options.MaxDt);
    }

    /// <summary>Returns peaks as frame indices and frequency-bin indices.</summary>
    public List<(int Time, int Freq)> ExtractPeaks(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var peaks = new List<(int Time, int Freq)>();
        var spectrum = new Complex[FingerprintOptions.WindowSize];
        var magnitudes = new double[FrequencyBins];

        for (var start = 0; start <= samples.Length - spectrum.Length; start += FingerprintOptions.Hop)
        {
            for (var i = 0; i < spectrum.Length; i++)
                spectrum[i] = new Complex(samples[start + i] * HannWindow[i], 0);

            Fourier.Forward(spectrum, FourierOptions.NoScaling);

            double sum = 0;
            for (var bin = 1; bin < FrequencyBins; bin++)
            {
                magnitudes[bin] = spectrum[bin].Magnitude;
                sum += magnitudes[bin];
            }

            var threshold = sum / (FrequencyBins - 1) * _options.PeakThreshold;
            for (var band = 1; band < _bandEdges.Length; band++)
            {
                var peakBin = _bandEdges[band - 1];
                for (var bin = peakBin + 1; bin < _bandEdges[band]; bin++)
                {
                    if (magnitudes[bin] > magnitudes[peakBin])
                        peakBin = bin;
                }

                if (magnitudes[peakBin] > threshold)
                    peaks.Add((start / FingerprintOptions.Hop, peakBin));
            }
        }

        return peaks;
    }

    public IEnumerable<(uint Hash, int Time)> GenerateHashes(IEnumerable<(int Time, int Freq)> peaks) =>
        GenerateHashes(peaks, _options.FanOut, _options.MaxDt);

    /// <summary>Combines peaks from different frames and returns each hash with its anchor frame.</summary>
    public static IEnumerable<(uint Hash, int Time)> GenerateHashes(
        IEnumerable<(int Time, int Freq)> peaks, int fanOut, int maxDt)
    {
        ArgumentNullException.ThrowIfNull(peaks);
        ValidatePairing(fanOut, maxDt);
        var ordered = peaks.OrderBy(peak => peak.Time).ThenBy(peak => peak.Freq).ToArray();
        foreach (var peak in ordered)
        {
            if (peak.Time < 0 || (uint)peak.Freq >= FrequencyBins)
                throw new ArgumentException("Peak times must be nonnegative and frequencies must fit in 10 bits.", nameof(peaks));
        }

        var nextFrame = 0;
        for (var i = 0; i < ordered.Length; i++)
        {
            var anchor = ordered[i];
            while (nextFrame < ordered.Length && ordered[nextFrame].Time == anchor.Time)
                nextFrame++;

            var targets = 0;
            for (var targetIndex = nextFrame; targetIndex < ordered.Length && targets < fanOut; targetIndex++)
            {
                var target = ordered[targetIndex];
                var delta = target.Time - anchor.Time;
                if (delta > maxDt)
                    break;

                var hash = ((uint)anchor.Freq << 18) | ((uint)target.Freq << 8) | (uint)delta;
                yield return (hash, anchor.Time);
                targets++;

            }
        }
    }

    private static void ValidatePairing(int fanOut, int maxDt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fanOut);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDt, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDt, MaxEncodedDelta);
    }

    private static double[] CreateHannWindow()
    {
        var window = new double[FingerprintOptions.WindowSize];
        for (var i = 0; i < window.Length; i++)
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (window.Length - 1)));

        return window;
    }
}
