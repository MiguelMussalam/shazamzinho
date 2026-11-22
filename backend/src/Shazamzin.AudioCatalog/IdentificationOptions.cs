namespace Shazamzin.AudioCatalog;

public sealed class IdentificationOptions
{
    public const string SectionName = "Identification";
    public int MinScore { get; init; } = 20;
    public int DeltaBucketMs { get; init; } = 50;
}

public sealed record IdentifyResult(string Title, string? Artist, int Score, double OffsetSeconds);
