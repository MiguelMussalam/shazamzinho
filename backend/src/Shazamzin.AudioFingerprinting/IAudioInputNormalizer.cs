namespace Shazamzin.AudioFingerprinting;

public interface IAudioInputNormalizer
{
    /// <summary>Converts encoded audio or explicitly identified raw PCM into mono samples at 11,025 Hz.</summary>
    Task<float[]> NormalizeAsync(ReadOnlyMemory<byte> audio, string? formatHint, CancellationToken cancellationToken);
}
