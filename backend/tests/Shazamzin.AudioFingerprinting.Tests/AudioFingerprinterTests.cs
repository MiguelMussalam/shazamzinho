using Xunit;

namespace Shazamzin.AudioFingerprinting.Tests;

public sealed class AudioFingerprinterTests
{
    private static readonly int[][] Notes =
    [
        [16, 32, 72, 143, 276, 512, 851],
        [19, 41, 65, 162, 311, 640, 884],
        [13, 38, 81, 129, 257, 470, 923],
        [21, 44, 57, 176, 334, 571, 802],
        [10, 28, 88, 112, 210, 436, 963]
    ];

    [Fact]
    public void IdenticalAudioProducesIdenticalHashes()
    {
        var fingerprinter = new AudioFingerprinter();
        var samples = CreateSignal();
        var first = fingerprinter.GenerateHashes(fingerprinter.ExtractPeaks(samples)).ToArray();
        var second = fingerprinter.GenerateHashes(fingerprinter.ExtractPeaks((float[])samples.Clone())).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void OffGridExcerptSharesHashesAtTheExpectedOffset()
    {
        const int hop = FingerprintOptions.Hop;
        const int offsetFrames = 79;
        const int offGridSamples = 177;
        var fingerprinter = new AudioFingerprinter();
        var full = CreateSignal();
        var excerpt = full.AsSpan(offsetFrames * hop + offGridSamples, 100 * hop).ToArray();

        var reference = fingerprinter.GenerateHashes(fingerprinter.ExtractPeaks(full)).ToHashSet();
        var query = fingerprinter.GenerateHashes(fingerprinter.ExtractPeaks(excerpt)).ToArray();
        var aligned = query.Count(hit => reference.Contains((hit.Hash, hit.Time + offsetFrames)));
        var fraction = aligned / (double)query.Length;

        Assert.NotEmpty(query);
        Assert.True(fraction >= 0.60, $"Aligned hash overlap was {fraction:P1}; expected at least 60%.");
    }

    [Fact]
    public void SilenceProducesNoPeaks()
    {
        var fingerprinter = new AudioFingerprinter();
        Assert.Empty(fingerprinter.ExtractPeaks(new float[FingerprintOptions.TargetSampleRate * 3]));
    }

    [Fact]
    public void HashPackingUsesTenTenAndEightBits()
    {
        var packed = AudioFingerprinter.GenerateHashes([(257, 1023), (2, 1023)], 1, 255).Single();

        Assert.Equal(2, packed.Time);
        Assert.Equal(1023u, (packed.Hash >> 18) & 1023);
        Assert.Equal(1023u, (packed.Hash >> 8) & 1023);
        Assert.Equal(255u, packed.Hash & 255);
        Assert.Throws<ArgumentException>(() => AudioFingerprinter.GenerateHashes([(0, 1024)], 1, 32).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioFingerprinter.GenerateHashes([], 1, 256).ToArray());
    }

    [Fact]
    public void HashPairingRespectsFanOutAndMaximumTimeDistance()
    {
        var peaks = new (int Time, int Freq)[] { (0, 24), (2, 42), (2, 64), (8, 96) };
        var hashes = AudioFingerprinter.GenerateHashes(peaks, 2, 3).ToArray();

        Assert.Equal(2, hashes.Length);
        Assert.All(hashes, value =>
        {
            Assert.Equal(0, value.Time);
            Assert.Equal(2u, value.Hash & 255);
        });
    }

    private static float[] CreateSignal()
    {
        const int noteLength = FingerprintOptions.Hop * 11;
        const int fadeLength = FingerprintOptions.Hop;
        const int windowSize = FingerprintOptions.WindowSize;
        var samples = new float[FingerprintOptions.TargetSampleRate * 12];
        for (var i = 0; i < samples.Length; i++)
        {
            var note = i / noteLength;
            var elapsed = i % noteLength;
            var current = Notes[(note * note + 3 * note) % Notes.Length];
            var previous = note == 0 ? current : Notes[((note - 1) * (note - 1) + 3 * (note - 1)) % Notes.Length];
            var mix = Math.Min(1.0, (double)elapsed / fadeLength);

            double sum = 0;
            for (var band = 0; band < current.Length; band++)
            {
                var angle = 2 * Math.PI * i / windowSize;
                sum += Math.Sin(angle * current[band]) * mix +
                    Math.Sin(angle * previous[band]) * (1 - mix);
            }

            samples[i] = (float)(sum * 0.1);
        }

        return samples;
    }
}
