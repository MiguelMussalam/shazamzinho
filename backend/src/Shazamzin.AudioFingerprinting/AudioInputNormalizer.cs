using FFMpegCore;
using FFMpegCore.Exceptions;
using FFMpegCore.Pipes;

namespace Shazamzin.AudioFingerprinting;

public sealed class AudioInputNormalizer : IAudioInputNormalizer
{
    public async Task<float[]> NormalizeAsync(
        ReadOnlyMemory<byte> audio, string? formatHint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (audio.IsEmpty)
            throw new ArgumentException("The audio input is empty.", nameof(audio));

        var format = formatHint?.Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(format))
            format = DetectFormat(audio.Span);

        return format switch
        {
            "wav" or "wave" or "audio/wav" or "audio/wave" or "audio/x-wav" =>
                WavePcm.Read(audio.Span, cancellationToken),
            "f32le-8000-mono" => WavePcm.ReadSamples(audio.Span, 8_000, 1, 32, true, cancellationToken),
            "mp3" or "audio/mpeg" or "webm" or "audio/webm" or "opus" or "audio/opus" or
                "ogg" or "audio/ogg" or "flac" or "audio/flac" =>
                await DecodeCompressedAsync(audio, cancellationToken),
            _ => throw new NotSupportedException("Unknown or unsupported audio format. For raw PCM, supply an explicit format hint.")
        };
    }

    private static string? DetectFormat(ReadOnlySpan<byte> audio)
    {
        if (WavePcm.HasHeader(audio)) return "wav";
        if (audio.StartsWith("OggS"u8)) return "ogg";
        if (audio.StartsWith("fLaC"u8)) return "flac";
        ReadOnlySpan<byte> webmHeader = [0x1A, 0x45, 0xDF, 0xA3];
        if (audio.StartsWith(webmHeader)) return "webm";
        if (audio.StartsWith("ID3"u8) ||
            (audio.Length >= 4 && audio[0] == 0xff && (audio[1] & 0xe0) == 0xe0 && (audio[1] & 0x06) != 0))
            return "mp3";

        return null;
    }

    private static async Task<float[]> DecodeCompressedAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(audio.ToArray(), writable: false);
        using var wave = new MemoryStream();
        try
        {
            await FFMpegArguments
                .FromPipeInput(new StreamPipeSource(input))
                .OutputToPipe(new StreamPipeSink(wave), options => options
                    .WithCustomArgument(
                        FormattableString.Invariant($"-vn -ac 1 -ar {FingerprintOptions.TargetSampleRate} -c:a pcm_s16le"))
                    .ForceFormat("wav"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();
        }
        catch (FFMpegException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Compressed audio could not be decoded. Check that the input is valid and FFmpeg is on PATH.", error);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return WavePcm.Read(wave.GetBuffer().AsSpan(0, checked((int)wave.Length)), cancellationToken, piped: true);
    }
}
