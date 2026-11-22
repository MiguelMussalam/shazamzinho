using System.Buffers.Binary;

namespace Shazamzin.AudioFingerprinting;

internal static class WavePcm
{
    private const ushort IntegerPcm = 1;
    private const ushort FloatPcm = 3;
    private const ushort Extensible = 0xfffe;
    private static readonly Guid IntegerPcmGuid = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatPcmGuid = new("00000003-0000-0010-8000-00aa00389b71");

    public static bool HasHeader(ReadOnlySpan<byte> wave) =>
        wave.Length >= 12 && wave[..4].SequenceEqual("RIFF"u8) && wave[8..12].SequenceEqual("WAVE"u8);

    public static float[] Read(ReadOnlySpan<byte> wave, CancellationToken cancellationToken, bool piped = false)
    {
        if (!HasHeader(wave))
            throw new FormatException("Expected a little-endian RIFF/WAVE header.");

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(wave[4..]);
        if (!(piped && riffSize == uint.MaxValue))
        {
            if (riffSize < 4 || riffSize > wave.Length - 8)
                throw new FormatException("The WAV container size is invalid.");

            wave = wave[..(8 + (int)riffSize)];
        }

        ReadOnlySpan<byte> format = default;
        ReadOnlySpan<byte> pcm = default;
        var hasData = false;
        var offset = 12;
        while (wave.Length - offset >= 8)
        {
            var chunk = wave.Slice(offset, 4);
            var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(wave[(offset + 4)..]);
            offset += 8;
            // FFmpeg cannot seek back to fill in the length of WAV data sent through a pipe.
            var size = piped && declaredSize == uint.MaxValue && chunk.SequenceEqual("data"u8)
                ? (uint)(wave.Length - offset)
                : declaredSize;
            if (size > wave.Length - offset)
                throw new FormatException("A WAV chunk extends beyond the input.");

            if (chunk.SequenceEqual("fmt "u8))
                format = wave.Slice(offset, (int)size);
            if (chunk.SequenceEqual("data"u8))
            {
                pcm = wave.Slice(offset, (int)size);
                hasData = true;
            }

            if (format.Length >= 16 && hasData)
                break;

            offset += (int)size;
            if ((size & 1) != 0 && offset < wave.Length)
                offset++;
        }

        if (format.Length < 16 || !hasData)
            throw new FormatException("The WAV format or audio data chunk is missing.");

        var encoding = BinaryPrimitives.ReadUInt16LittleEndian(format);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(format[4..]);
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
        var sampleBits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);

        if (encoding == Extensible)
        {
            if (format.Length < 40 || BinaryPrimitives.ReadUInt16LittleEndian(format[16..]) < 22)
                throw new FormatException("The WAV extensible format header is incomplete.");

            var subtype = new Guid(format[24..40]);
            if (subtype == IntegerPcmGuid) encoding = IntegerPcm;
            else if (subtype == FloatPcmGuid) encoding = FloatPcm;
        }

        if (encoding is not (IntegerPcm or FloatPcm))
            throw new NotSupportedException("The WAV samples must use integer PCM or IEEE floating-point PCM.");
        if (blockAlign != channels * (sampleBits / 8))
            throw new FormatException("The WAV block alignment is invalid.");

        return ReadSamples(pcm, sampleRate, channels, sampleBits, encoding == FloatPcm, cancellationToken);
    }

    public static float[] ReadSamples(
        ReadOnlySpan<byte> pcm, int sampleRate, int channels, int sampleBits, bool floatingPoint,
        CancellationToken cancellationToken)
    {
        if (sampleRate <= 0 || channels <= 0)
            throw new FormatException("PCM requires a positive sample rate and channel count.");
        if (floatingPoint ? sampleBits is not (32 or 64) : sampleBits is not (8 or 16 or 24 or 32))
            throw new NotSupportedException("Unsupported PCM sample size.");

        var sampleBytes = sampleBits / 8;
        var frameBytes = sampleBytes * channels;
        if (pcm.Length % frameBytes != 0)
            throw new FormatException("PCM data ends in an incomplete sample frame.");

        var frameCount = pcm.Length / frameBytes;
        if (frameCount < (long)FingerprintOptions.MinimumDurationSeconds * sampleRate)
            throw new FormatException($"Audio must be at least {FingerprintOptions.MinimumDurationSeconds} seconds long.");

        var mono = new float[frameCount];
        var offset = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            if ((frame & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();

            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = pcm.Slice(offset, sampleBytes);
                var value = DecodeSample(sample, floatingPoint);
                if (!double.IsFinite(value))
                    throw new FormatException("PCM contains an invalid floating-point sample.");

                sum += Math.Clamp(value, -1, 1);
                offset += sampleBytes;
            }

            mono[frame] = (float)(sum / channels);
        }

        return Resample(mono, sampleRate, cancellationToken);
    }

    private static double DecodeSample(ReadOnlySpan<byte> sample, bool floatingPoint)
    {
        if (floatingPoint)
        {
            return sample.Length == 4
                ? BinaryPrimitives.ReadSingleLittleEndian(sample)
                : BinaryPrimitives.ReadDoubleLittleEndian(sample);
        }

        return sample.Length switch
        {
            1 => (sample[0] - 128) / 128.0,
            2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32_768.0,
            3 => ((sample[0] | (sample[1] << 8) | (sample[2] << 16)) << 8 >> 8) / 8_388_608.0,
            _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2_147_483_648.0
        };
    }

    private static float[] Resample(float[] mono, int sourceRate, CancellationToken cancellationToken)
    {
        const int targetRate = FingerprintOptions.TargetSampleRate;
        if (sourceRate == targetRate)
            return mono;

        // Windowed sinc interpolation also removes frequencies above the new Nyquist limit.
        var cutoff = Math.Min(1.0, (double)targetRate / sourceRate);
        var radius = 16 / cutoff;
        var output = new float[checked((int)((long)mono.Length * targetRate / sourceRate))];
        for (var i = 0; i < output.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();

            var position = (double)i * sourceRate / targetRate;
            var first = (int)Math.Max(0, Math.Ceiling(position - radius));
            var last = (int)Math.Min(mono.Length - 1, Math.Floor(position + radius));
            double sum = 0;
            double weightSum = 0;
            for (var index = first; index <= last; index++)
            {
                var distance = index - position;
                var angle = Math.PI * distance * cutoff;
                var sinc = angle == 0 ? 1 : Math.Sin(angle) / angle;
                var weight = sinc * (0.5 + 0.5 * Math.Cos(Math.PI * distance / radius));
                sum += weight * mono[index];
                weightSum += weight;
            }

            output[i] = (float)Math.Clamp(sum / weightSum, -1, 1);
        }

        return output;
    }
}
