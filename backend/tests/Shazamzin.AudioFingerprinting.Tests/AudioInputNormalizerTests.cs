using System.Buffers.Binary;
using Xunit;

namespace Shazamzin.AudioFingerprinting.Tests;

public sealed class AudioInputNormalizerTests
{
    private readonly AudioInputNormalizer _normalizer = new();

    [Fact]
    public async Task ReadsStereoWaveWithAnOddSizedMetadataChunkAndResamplesToMono()
    {
        var wave = CreateStereoWave(22_050, 3, 16_384, 0);
        var samples = await _normalizer.NormalizeAsync(wave, null, CancellationToken.None);

        Assert.Equal(3 * FingerprintOptions.TargetSampleRate, samples.Length);
        Assert.All(samples, sample => Assert.InRange(sample, 0.24999f, 0.25001f));
    }

    [Fact]
    public async Task ReadsExplicitEightKilohertzRawFloatPcm()
    {
        var raw = new byte[3 * 8_000 * sizeof(float)];
        for (var offset = 0; offset < raw.Length; offset += sizeof(float))
            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(offset), -0.5f);

        var samples = await _normalizer.NormalizeAsync(raw, "f32le-8000-mono", CancellationToken.None);

        Assert.Equal(3 * FingerprintOptions.TargetSampleRate, samples.Length);
        Assert.All(samples, sample => Assert.InRange(sample, -0.50001f, -0.49999f));
    }

    [Fact]
    public async Task RejectsEmptyUnknownShortAndTruncatedInputs()
    {
        var shortWave = CreateStereoWave(11_025, 2, 0, 0);
        var truncatedWave = CreateStereoWave(11_025, 3, 0, 0)[..^1];

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _normalizer.NormalizeAsync(ReadOnlyMemory<byte>.Empty, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _normalizer.NormalizeAsync("not audio"u8.ToArray(), null, CancellationToken.None));
        var error = await Assert.ThrowsAsync<FormatException>(() =>
            _normalizer.NormalizeAsync(shortWave, null, CancellationToken.None));
        Assert.Contains("at least 3 seconds", error.Message);
        await Assert.ThrowsAsync<FormatException>(() =>
            _normalizer.NormalizeAsync(truncatedWave, null, CancellationToken.None));
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _normalizer.NormalizeAsync(ReadOnlyMemory<byte>.Empty, null, cancellation.Token));
    }

    private static byte[] CreateStereoWave(int sampleRate, int seconds, short left, short right)
    {
        var frameCount = sampleRate * seconds;
        const int frameSize = 4;
        var dataSize = frameSize * frameCount;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write("RIFF"u8);
        writer.Write(46 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("JUNK"u8);
        writer.Write(1);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        writer.Write(sampleRate);
        writer.Write(sampleRate * frameSize);
        writer.Write((ushort)frameSize);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        for (var i = 0; i < frameCount; i++)
        {
            writer.Write(left);
            writer.Write(right);
        }

        return buffer.ToArray();
    }
}
