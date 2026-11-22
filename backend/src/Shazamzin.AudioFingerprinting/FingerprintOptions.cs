namespace Shazamzin.AudioFingerprinting;

public sealed class FingerprintOptions
{
    public const int TargetSampleRate = 11_025;
    public const int WindowSize = 2_048;
    public const int Hop = 512;
    public const int MinimumDurationSeconds = 3;

    public int FanOut { get; init; } = 10;
    public int MaxDt { get; init; } = 32;
    public IReadOnlyList<int> BandEdges { get; init; } = [1, 24, 48, 96, 192, 384, 768, 1_024];

    // Each band peak must exceed the mean magnitude of the frame times this multiplier.
    public double PeakThreshold { get; init; } = 1.0;
}
